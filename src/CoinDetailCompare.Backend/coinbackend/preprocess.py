"""Coin preprocessing: detection, segmentation, background normalization,
rotation alignment, crop & resize, then embedding hand-off.

Heavy import (OpenCV) is lazy so the rest of the package can be imported
and syntax-checked without it installed.
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np


def _cv2():
    try:
        import cv2  # type: ignore
        return cv2
    except Exception as exc:  # pragma: no cover - env dependent
        raise RuntimeError(
            "OpenCV is required for preprocessing. Install: pip install opencv-python"
        ) from exc


def _read_image(path: str | Path) -> "np.ndarray":
    cv2 = _cv2()
    img = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if img is None:
        raise FileNotFoundError(f"Could not read image: {path}")
    return img


def _write_image(path: str | Path, img: "np.ndarray") -> Path:
    cv2 = _cv2()
    out = Path(path)
    out.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(out), img)
    return out


def detect_coin(img: "np.ndarray"):
    """Detect the coin circle/contour.

    Returns (mask, shape) where mask is an HxW uint8 image (255 inside the coin)
    and shape is 'circle' | 'ellipse' | 'contour'.
    """
    cv2 = _cv2()
    h, w = img.shape[:2]
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    gray = cv2.GaussianBlur(gray, (9, 9), 2)

    # 1) HoughCircles for round coins
    min_r = int(min(h, w) * 0.18)
    max_r = int(min(h, w) * 0.70)
    circles = cv2.HoughCircles(
        gray,
        cv2.HOUGH_GRADIENT,
        dp=1.2,
        minDist=min(h, w) * 0.5,
        param1=120,
        param2=60,
        minRadius=min_r,
        maxRadius=max_r,
    )
    if circles is not None:
        # HoughCircles returns candidates in arbitrary order. The coin is
        # aligned with the viewfinder's template circle, i.e. the frame
        # center: prefer the candidate closest to the center, breaking
        # ties by radius (bigger wins).
        cx_f, cy_f = w / 2.0, h / 2.0
        half = min(h, w) / 2.0
        best = None
        for c in circles[0]:
            cx, cy, r = float(c[0]), float(c[1]), float(c[2])
            d = math.hypot(cx - cx_f, cy - cy_f) / half
            score = d * 2.0 - r / half  # lower is better
            if best is None or score < best[0]:
                best = (score, cx, cy, r)
        _, cx, cy, r = best
        cx, cy = int(round(cx)), int(round(cy))
        # Hough tends to land on the inner rim edge; grow the radius so the
        # rim (гурт) is always inside the crop. Clamped to image bounds.
        r = int(round(r * 1.08))
        r = min(r, cx, cy, w - cx, h - cy)
        mask = np.zeros((h, w), dtype=np.uint8)
        cv2.circle(mask, (cx, cy), r, 255, -1)
        return mask, "circle"

    # 2) Contour fallback (handles non-circular coins)
    edges = cv2.Canny(gray, 60, 160)
    edges = cv2.dilate(edges, np.ones((3, 3), np.uint8), iterations=2)
    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if contours:
        # Same center preference as Hough: among coin-sized contours pick
        # the one whose mass center is closest to the frame center, so an
        # off-center object (table edge, another round item) wins out.
        frame_area = float(h * w)
        scored = []
        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < frame_area * 0.03 or area > frame_area * 0.75:
                continue
            m = cv2.moments(cnt)
            if m["m00"] <= 0:
                continue
            mx = m["m10"] / m["m00"]
            my = m["m01"] / m["m00"]
            scored.append((math.hypot(mx - w / 2.0, my - h / 2.0), cnt))
        contour = min(scored, key=lambda t: t[0])[1] if scored else max(contours, key=cv2.contourArea)
        if cv2.contourArea(contour) > (h * w) * 0.05:
            mask = np.zeros((h, w), dtype=np.uint8)
            cv2.drawContours(mask, [contour], -1, 255, -1)
            # Prefer an inscribed/circumscribed circle for near-round contours
            (cx, cy), r = cv2.minEnclosingCircle(contour)
            if _is_roundish(contour, (cx, cy), r):
                # Small margin: Canny may stop just inside the rim.
                r = min(int(round(r * 1.03)), int(cx), int(cy), w - int(cx), h - int(cy))
                cv2.circle(mask, (int(cx), int(cy)), r, 255, -1)
                return mask, "circle"
            return mask, "contour"

    # 3) Last resort: central square region
    s = int(min(h, w) * 0.6)
    x0 = (w - s) // 2
    y0 = (h - s) // 2
    mask = np.zeros((h, w), dtype=np.uint8)
    mask[y0 : y0 + s, x0 : x0 + s] = 255
    return mask, "square"


def _is_roundish(contour: "np.ndarray", center, radius: float) -> bool:
    cv2 = _cv2()
    area = cv2.contourArea(contour)
    circle_area = math.pi * radius * radius
    if circle_area <= 0:
        return False
    return 0.85 <= area / circle_area <= 1.15


def normalize_background(img: "np.ndarray", mask: "np.ndarray", color: tuple[int, int, int]) -> "np.ndarray":
    """Fill everything outside the coin mask with a neutral color."""
    out = img.copy()
    bg = np.zeros_like(img)
    bg[:, :] = color
    m = mask > 0
    out[~m] = bg[~m]
    return out


def rotate_image(img: "np.ndarray", angle_deg: float) -> "np.ndarray":
    """Rotate around the image center, keeping canvas size."""
    cv2 = _cv2()
    h, w = img.shape[:2]
    center = (w / 2.0, h / 2.0)
    m = cv2.getRotationMatrix2D(center, angle_deg, 1.0)
    return cv2.warpAffine(img, m, (w, h), flags=cv2.INTER_LINEAR)


def crop_square(img: "np.ndarray", mask: "np.ndarray") -> "np.ndarray":
    """Crop to the smallest square bounding the coin mask."""
    ys, xs = np.where(mask > 0)
    if len(xs) == 0:
        return img
    x0, x1 = int(xs.min()), int(xs.max())
    y0, y1 = int(ys.min()), int(ys.max())
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    half = max(x1 - x0, y1 - y0) // 2 + 1
    h, w = img.shape[:2]
    nx0 = max(0, cx - half)
    ny0 = max(0, cy - half)
    nx1 = min(w, cx + half)
    ny1 = min(h, cy + half)
    return img[ny0:ny1, nx0:nx1]


def resize_keep_square(img: "np.ndarray", size: int) -> "np.ndarray":
    cv2 = _cv2()
    h, w = img.shape[:2]
    if h != w:
        s = min(h, w)
        img = img[(h - s) // 2 : (h - s) // 2 + s, (w - s) // 2 : (w - s) // 2 + s]
    if size < min(h, w):
        img = cv2.resize(img, (size, size), interpolation=cv2.INTER_AREA)
    else:
        img = cv2.resize(img, (size, size), interpolation=cv2.INTER_CUBIC)
    return img


def auto_align_angle(img: "np.ndarray", etalon: "np.ndarray | None", max_deg: float = 30.0) -> float:
    """Estimate rotation to align `img` to `etalon` (if given).

    Phase 1 baseline: coarse-to-fine correlation of a downsampled grayscale
    image over candidate angles. To be replaced / extended by RANSAC on
    DINOv3 patches once real embedding is bound (Phase 2+).
    """
    cv2 = _cv2()
    if etalon is None:
        return 0.0
    small = 128
    a = cv2.resize(img, (small, small))
    b = cv2.resize(etalon, (small, small))
    a = cv2.cvtColor(a, cv2.COLOR_BGR2GRAY).astype("float32")
    b = cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype("float32")
    center = (small / 2.0, small / 2.0)
    b_flat = b.ravel()

    def score_at(deg: float) -> float:
        m = cv2.getRotationMatrix2D(center, deg, 1.0)
        ra = cv2.warpAffine(a, m, (small, small), flags=cv2.INTER_LINEAR).ravel()
        c = float(np.corrcoef(ra, b_flat)[0, 1])
        return c if not math.isnan(c) else -1.0

    # Coarse scan: integer degrees
    best = 0.0
    best_score = score_at(0.0)
    for deg in range(-int(max_deg), int(max_deg) + 1):
        s = score_at(float(deg))
        if s > best_score:
            best_score, best = s, float(deg)
    # Fine pass: 0.25 deg steps around the coarse winner
    deg = best - 1.0
    while deg <= best + 1.0:
        s = score_at(deg)
        if s > best_score:
            best_score, best = s, deg
        deg += 0.25
    return best


def preprocess(
    raw_path: str | Path,
    side: str,
    target_size: int,
    min_size: int,
    background_color: tuple[int, int, int],
    orientation_deg: float = 0.0,
    etalon_path: str | Path | None = None,
) -> dict:
    """Run the full preprocessing pipeline and return metadata (no embedding here)."""
    target_size = max(int(target_size), int(min_size))
    img = _read_image(raw_path)

    etalon = _read_image(etalon_path) if etalon_path else None
    if orientation_deg:
        angle = float(orientation_deg)
    else:
        angle = auto_align_angle(img, etalon)

    mask, shape = detect_coin(img)
    if abs(angle) > 0.01:
        img = rotate_image(img, angle)
        # re-detect after rotation for a tight crop
        mask, shape = detect_coin(img)

    img = normalize_background(img, mask, background_color)
    img = crop_square(img, mask)
    img = resize_keep_square(img, target_size)

    result = {
        "shape_detected": shape,
        "orientation_applied_deg": angle,
        "size": int(img.shape[0]),
        "image": img,  # BGR ndarray (transient; caller persists it)
    }
    return result
