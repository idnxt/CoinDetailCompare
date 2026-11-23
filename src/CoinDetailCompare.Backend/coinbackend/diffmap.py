"""Comparison layer between the query coin and an etalon.

When the user picks an etalon candidate, the app asks for:

1. the rotation-normalized query (canonical frame, rotated onto the etalon)
   and a pixel difference map between the two aligned coins;
2. a 2-D projection (PCA) of the search vectors: the query embedding plus
   the etalon-candidate embeddings, so the user can *see* where in the
   vector space the coin sits relative to its etalons.

All geometry reuses the canonical-frame helpers from anomaly.py.
"""
from __future__ import annotations

import numpy as np

from .anomaly import (
    _CANONICAL_RADIUS,
    _canonicalize,
    _cv2,
    _detect_coin_circle,
    _estimate_rotation,
)


def _disk_mask(h: int, w: int) -> "np.ndarray":
    """Float mask: 1.0 inside the coin disc of the canonical frame."""
    cv2 = _cv2()
    mask = np.zeros((h, w), "float32")
    # The canonical coin radius is _CANONICAL_RADIUS * full canvas size
    # (see _canonical_transform): the coin fills ~88% of the canonical frame.
    cv2.circle(mask, (w // 2, h // 2), int(_CANONICAL_RADIUS * min(h, w)), 1.0, -1)
    return mask


def aligned_pair(
    query: "np.ndarray", etalon: "np.ndarray"
) -> tuple["np.ndarray", "np.ndarray", float, "np.ndarray"]:
    """Canonicalize both frames and rotate the query onto the etalon.

    Returns (normalized query, etalon in canonical frame, angle in degrees,
    forward matrix mapping the *original* query frame to the canonical
    frame — needed to warp the diff layer back onto the raw crop, because
    in the canonical frame the coin occupies only ~44% of the canvas while
    in the raw crop it fills almost the whole image).
    """
    cv2 = _cv2()
    qc, m_q = _canonicalize(query, _detect_coin_circle(query))
    rc, _ = _canonicalize(etalon, _detect_coin_circle(etalon))
    deg = _estimate_rotation(qc, rc)
    h, w = qc.shape[:2]
    m = cv2.getRotationMatrix2D((w / 2.0, h / 2.0), deg, 1.0)
    qn = cv2.warpAffine(qc, m, (w, h), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    return qn, rc, float(deg), m_q


def diff_image(a: "np.ndarray", b: "np.ndarray") -> tuple["np.ndarray", float]:
    """Red-tinted |a - b| map, normalized to 0..255 inside the coin disc.

    The background (outside the disc) is black so the layer can be overlaid
    on the query photo. Returns (bgr image, mean difference 0..1).
    """
    cv2 = _cv2()
    ga = cv2.cvtColor(a, cv2.COLOR_BGR2GRAY).astype("float32")
    gb = cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype("float32")
    d = np.abs(ga - gb)
    mask = _disk_mask(*d.shape[:2])
    inside = mask > 0
    peak = float(d[inside].max()) if inside.any() else 0.0
    if peak > 1e-6:
        d = d / peak * 255.0
    d = (d * mask).astype("uint8")
    mean = float(d[inside].mean()) / 255.0 if inside.any() else 0.0
    # black-blue background, strong red where the coins differ
    out = np.zeros((d.shape[0], d.shape[1], 3), "uint8")
    out[..., 2] = np.minimum(255, (d * 1.5)).astype("uint8")
    return out, mean


def back_to_query(diff: "np.ndarray", m_q: "np.ndarray", size: tuple[int, int]) -> "np.ndarray":
    """Warp a canonical-frame diff layer back onto the original query frame,
    so the overlay lines up with the raw crop shown in the UI (there the
    coin fills almost the whole image, in the canonical frame only ~88%).

    warpAffine applies dst(p) = src(M^-1 p), i.e. M must be the *forward*
    transform source -> destination. The layer lives in the canonical frame
    and must land in the query frame, so the forward transform is m_q^-1
    (m_q itself maps query -> canonical)."""
    cv2 = _cv2()
    return cv2.warpAffine(
        diff, cv2.invertAffineTransform(m_q), size, flags=cv2.INTER_LINEAR,
        borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0),
    )


def project_2d(vectors: list["np.ndarray"]) -> list[tuple[float, float]]:
    """Project embedding vectors onto their first two principal components,
    scaled to the unit square (0..1)."""
    X = np.stack([v.astype("float64").ravel() for v in vectors])
    Xc = X - X.mean(axis=0)
    U, S, _Vt = np.linalg.svd(Xc, full_matrices=False)
    P = U[:, :2] * S[:2]  # coordinates in the top-2 principal-component basis
    lo, hi = P.min(axis=0), P.max(axis=0)
    span = np.where(hi - lo < 1e-12, 1.0, hi - lo)
    Q = (P - lo) / span
    return list(zip(Q[:, 0].tolist(), Q[:, 1].tolist()))
