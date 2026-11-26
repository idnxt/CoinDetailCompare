"""Anomaly detection.

Production priority: SuperADD (anomalib backbone, ViT-H+/16) with memory bank
and heatmaps. Fallback: AnomalyDINO (training-free, DINOv2/v3 B/L).

Dev fallback implemented here: patch-level cosine distance to a memory bank
built from 1..5 normal reference crops, producing a 64x64 / 128x128 heatmap.
This is a working approximation so the pipeline (heatmap, bbox, UI overlay)
can be exercised end-to-end before the production detector is bound (Phase 4).
"""
from __future__ import annotations

from pathlib import Path

import numpy as np

from .config import Config
from .embed import BaseEmbedder
from .storage import Storage


# ---- geometric alignment helpers ------------------------------------------
# Photos of the same coin are cropped with slightly different rim margins
# (HoughCircles radius noise, center offset), so the coin diameter differs
# by a few percent between the query and the reference crops. Comparing
# patches directly would flag every shifted edge (letter strokes, rim) as an
# anomaly. Both sides are therefore normalized to a canonical frame: coin
# circle centered, fixed radius.
_CANONICAL_RADIUS = 0.44  # coin radius as a fraction of the canvas half-size


def _detect_coin_circle(img: "np.ndarray") -> tuple[float, float, float]:
    """Estimate the coin rim circle (cx, cy, r) inside a saved square crop."""
    cv2 = _cv2()
    h, w = img.shape[:2]
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    gray = cv2.GaussianBlur(gray, (9, 9), 2)
    min_r = int(min(h, w) * 0.25)
    max_r = int(min(h, w) * 0.70)
    circles = cv2.HoughCircles(
        gray,
        cv2.HOUGH_GRADIENT,
        dp=1.2,
        minDist=min(h, w) * 0.5,
        param1=100,
        param2=50,
        minRadius=min_r,
        maxRadius=max_r,
    )
    if circles is not None:
        c = circles[0, 0]
        return float(c[0]), float(c[1]), float(c[2])
    # Fallback: centered circle filling ~92% of the crop (the typical result
    # of the preprocessing pipeline).
    return w / 2.0, h / 2.0, min(h, w) * 0.46


def _canonical_transform(img: "np.ndarray", circle: tuple[float, float, float]) -> "np.ndarray":
    """2x3 similarity matrix mapping `img`'s coin circle to the canonical
    circle (canvas center, radius _CANONICAL_RADIUS * half-size)."""
    cx, cy, r = circle
    h, w = img.shape[:2]
    s = (_CANONICAL_RADIUS * min(h, w)) / r if r > 1e-6 else 1.0
    return np.float32(
        [
            [s, 0.0, w / 2.0 - s * cx],
            [0.0, s, h / 2.0 - s * cy],
        ]
    )


def _canonicalize(img: "np.ndarray", circle: tuple[float, float, float]) -> tuple["np.ndarray", "np.ndarray"]:
    """Warp `img` to the canonical frame. Returns (warped, forward matrix)."""
    cv2 = _cv2()
    h, w = img.shape[:2]
    m = _canonical_transform(img, circle)
    warped = cv2.warpAffine(
        img, m, (w, h), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE
    )
    return warped, m


def _compose(a: "np.ndarray", b: "np.ndarray") -> "np.ndarray":
    """Compose two 2x3 similarity matrices: apply `a` first, then `b`."""
    ab = b[:, :2] @ a[:, :2]
    t = b[:, :2] @ a[:, 2:3] + b[:, 2:3]
    return np.hstack((ab, t))


def _estimate_rotation(
    a: "np.ndarray",
    b: "np.ndarray",
    rim_offset: float = 0.10,
    n_windows: int = 6,
    coarse_deg: float = 10.0,
) -> float:
    """Coarse-to-fine angle (deg) to rotate `a` onto `b`.

    Both images must already be in the canonical frame (coin centered, fixed
    radius).  The whole coin disc is compared: a coarse 5-degree grid over
    the full circle, then a 0.5-degree refine of the two best candidates,
    then a 0.05-degree polish.  The background (outside the coin circle) is
    masked out, so different photo backgrounds do not bias the match.

    Matching the full design (not just a few rim marks) is what makes this
    robust on real photos: rim-only matching produced spurious peaks up to
    ~190 degrees off on near-symmetric reverse designs.

    `rim_offset` / `n_windows` are kept for signature compatibility with the
    former rim-circle implementation and are unused.
    The returned angle is the one to pass to cv2.getRotationMatrix2D for `a`.
    """
    cv2 = _cv2()
    s = 128
    a = cv2.resize(a, (s, s), interpolation=cv2.INTER_AREA)
    b = cv2.resize(b, (s, s), interpolation=cv2.INTER_AREA)
    ga = cv2.cvtColor(a, cv2.COLOR_BGR2GRAY).astype("float32")
    gb = cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype("float32")

    # Mask: the inner half of the coin disc in the canonical frame. The rim
    # ring is deliberately excluded: Hough-circle radius estimates can be off
    # by a few percent between frames, and with a full-disc mask that scale
    # mismatch at the rim would dominate the NCC and shift the best angle.
    # Matching the inner design (not just rim marks) is what keeps this
    # robust on real photos.
    mask = np.zeros((s, s), "float32")
    cv2.circle(mask, (s // 2, s // 2), int(_CANONICAL_RADIUS * s / 2), 1.0, -1)

    gb_c = (gb - gb.mean()) * mask
    gb_den = float(np.sqrt((gb_c * gb_c).sum()))

    def score(deg: float) -> float:
        # Rotate `a` by `deg` (same convention as getRotationMatrix2D) and
        # measure masked NCC against `b`.
        m = cv2.getRotationMatrix2D((s / 2.0, s / 2.0), deg, 1.0)
        ra = cv2.warpAffine(ga, m, (s, s), flags=cv2.INTER_LINEAR, borderValue=0.0)
        ra_c = (ra - ra.mean()) * mask
        den = float(np.sqrt((ra_c * ra_c).sum())) * gb_den
        return float((ra_c * gb_c).sum() / den) if den > 1e-9 else 0.0

    def _refine(center: float) -> tuple[float, float]:
        # 0.5 deg steps over +/-coarse_deg around `center`, then 0.05 deg
        # over the remaining +/-0.5 deg.
        best, best_s = center, score(center)
        d = center - coarse_deg
        while d <= center + coarse_deg:
            sc = score(d)
            if sc > best_s:
                best, best_s = d % 360.0, sc
            d += 0.5
        d = best - 0.5
        while d <= best + 0.5:
            sc = score(d)
            if sc > best_s:
                best, best_s = d % 360.0, sc
            d += 0.05
        return best, best_s

    # Coarse pass over the whole circle (5 deg grid).  The score profile of
    # a coin design can be multi-modal (near-symmetric patterns), so refine
    # the two best coarse candidates and take the global winner.
    coarse = [(d, score(d)) for d in range(0, 360, 5)]
    coarse.sort(key=lambda t: t[1], reverse=True)
    best, best_s = _refine(coarse[0][0])
    if len(coarse) > 1 and coarse[1][0] != coarse[0][0]:
        b2, s2 = _refine(coarse[1][0])
        if s2 > best_s:
            best, best_s = b2, s2
    return best % 360.0


class AnomalyDetector:
    def __init__(self, cfg: Config, storage: Storage, embedder: BaseEmbedder) -> None:
        self.cfg = cfg
        self.storage = storage
        self.embedder = embedder
        self._backend = None  # SuperADD/AnomalyDINO instance when available

    def detect(
        self,
        crop_path: str | Path,
        side: str,
        reference_ids: list[int],
        heatmap_size: int = 128,
        threshold: float | None = None,
        coin_id: int | None = None,
        device: str | None = None,
        patch_grid: int | None = None,
    ) -> dict:
        # Optional per-request device override (cpu/gpu), validated by a
        # tiny op (kernels may be missing for the GPU compute capability).
        dino = getattr(self.embedder, "model", None)
        orig_device = None
        if device is not None and dino is not None:
            try:
                import torch  # type: ignore

                target = torch.device("cuda" if device.lower() in ("gpu", "cuda") else "cpu")
                if target.type == "cuda":
                    probe = torch.zeros(1, device="cuda")
                    _ = probe + 1
                orig_device = dino.device
                dino.set_device(target)
            except Exception:
                pass  # keep the current device

        try:
            backend = self._get_backend()
            if backend is not None:
                name, det = backend
                try:
                    if name == "anomalydino":
                        return self._detect_anomalydino(
                            det, crop_path, side, heatmap_size, threshold, coin_id
                        )
                    if name == "superadd":
                        return self._detect_superadd(
                            det, crop_path, side, reference_ids, heatmap_size, threshold, coin_id
                        )
                except Exception:
                    # Broken / incompatible anomalib build: disable and fall back.
                    self._backend = None
            return self._detect_patch(
                crop_path, reference_ids, heatmap_size, threshold, side, coin_id,
                patch_grid=patch_grid,
            )
        finally:
            if orig_device is not None and dino is not None:
                try:
                    dino.set_device(orig_device)
                except Exception:
                    pass

    # ---- production backend selection (guarded: everything is optional) ----
    def _get_backend(self):
        if self._backend is not None:
            return self._backend
        want = self.cfg.anomaly_backend
        try:
            import torch  # noqa: F401  # type: ignore
        except Exception:
            self._backend = None
            return None

        if want in ("auto", "anomalydino"):
            try:
                from anomalib.models import AnomalyDINO  # type: ignore

                from .embed import _device  # verified cuda-or-cpu pick

                device = str(_device(self.cfg))
                det = AnomalyDINO(model_name="dinov2-small")
                det = det.to(device)
                det.eval()
                self._backend = ("anomalydino", det)
                return self._backend
            except Exception:
                pass

        if want in ("auto", "superadd"):
            try:
                from anomalib.models import SuperADD  # type: ignore

                self._backend = ("superadd", SuperADD())
                return self._backend
            except Exception:
                pass

        self._backend = None
        return None

    # ---- AnomalyDINO (anomalib, training-free) ----
    def _detect_anomalydino(self, det, crop_path, side, heatmap_size, threshold, coin_id):
        import cv2  # type: ignore

        thr = self.cfg.anomaly_threshold if threshold is None else threshold
        pred = det.predict(data=[str(crop_path)], return_anomaly_map=True)
        score = float(pred.pred_score[0])
        norm = self._map_to_numpy(pred.anomaly_map, heatmap_size)

        return self._finalize(
            norm, score, thr, heatmap_size, side, coin_id, crop_path
        )

    # ---- SuperADD (anomalib, trained on the normal reference crops) ----
    def _detect_superadd(self, model, crop_path, side, reference_ids, heatmap_size, threshold, coin_id):
        import shutil
        import tempfile

        import cv2  # type: ignore

        refs = [
            self.storage.photo_path(cid, side)
            for cid in reference_ids
            if self.storage.photo_path(cid, side).exists()
        ]
        if not refs:
            raise RuntimeError("no reference crops available for SuperADD training")

        # Train (or retrain) on the normal reference crops, lazily.
        with tempfile.TemporaryDirectory() as td:
            for p in refs:
                shutil.copyfile(p, Path(td) / p.name)
            from lightning.pytorch import Trainer  # type: ignore
            from anomalib.data import LightningDataModule  # type: ignore

            dm = LightningDataModule(folder_path=td, image_size=(256, 256), num_workers=0)
            trainer = Trainer(
                max_epochs=5,
                logger=False,
                enable_checkpointing=False,
                accelerator="auto",
                devices=1,
            )
            model.fit(dm.train_dataloader())

        pred = model.predict(data=[str(crop_path)], return_anomaly_map=True)
        score = float(pred.pred_score[0])
        norm = self._map_to_numpy(pred.anomaly_map, heatmap_size)
        thr = self.cfg.anomaly_threshold if threshold is None else threshold
        return self._finalize(norm, score, thr, heatmap_size, side, coin_id, crop_path)

    # ---- shared: anomaly map -> heatmap + boxes + persistence ----
    def _map_to_numpy(self, anomaly_map, size: int) -> "np.ndarray":
        import cv2  # type: ignore

        arr = anomaly_map
        # tensor (1,H,W) / (H,W,3) / (H,W) -> float HxW
        try:
            import torch  # type: ignore

            arr = torch.as_tensor(arr).float().cpu()
            if arr.ndim == 4:
                arr = arr[0]
            if arr.ndim == 3 and arr.shape[-1] == 3:
                arr = arr.mean(dim=-1)
            elif arr.ndim == 3:
                arr = arr[0]
            arr = arr.numpy()
        except Exception:
            pass
        arr = np.asarray(arr, dtype="float32")
        dmax = float(arr.max()) if arr.size else 0.0
        norm = arr / dmax if dmax > 0 else arr
        norm = cv2.resize(norm, (size, size), interpolation=cv2.INTER_LINEAR)
        return norm.astype("float32")

    def _finalize(self, norm, score, thr, heatmap_size, side, coin_id, crop_path):
        import cv2  # type: ignore

        is_anomaly = score >= thr
        boxes = self._find_boxes(norm, thr)

        heat_path = None
        heat_mat_path = None
        if coin_id is not None:
            heat_mat_path = str(self.storage.heatmap_matrix_path(coin_id, side))
            np.save(heat_mat_path, norm)
            query = cv2.imread(str(crop_path), cv2.IMREAD_COLOR)
            if query is not None:
                heat_img = self._to_colormap(norm, query.shape)
                heat_path = str(self.storage.heatmap_image_path(coin_id, side))
                cv2.imwrite(heat_path, heat_img)

        top = []
        for idx in np.argsort(-norm.ravel())[:8]:
            i, j = divmod(int(idx), heatmap_size)
            top.append({"row": i, "col": j, "distance": round(float(norm[i, j]), 4)})

        return {
            "is_anomaly": is_anomaly,
            "score": round(score, 4),
            "heatmap_path": heat_path,
            "heatmap_matrix_path": heat_mat_path,
            "heatmap_size": heatmap_size,
            "bounding_boxes": boxes,
            "top_patches": top,
        }

    # ---- dev fallback: patch-level distance to a memory bank ----
    def _detect_patch(
        self, crop_path, reference_ids, heatmap_size, threshold, side, coin_id,
        patch_grid: int | None = None,
    ):
        import cv2  # type: ignore

        thr = self.cfg.anomaly_threshold if threshold is None else threshold
        query = cv2.imread(str(crop_path), cv2.IMREAD_COLOR)
        if query is None:
            raise FileNotFoundError(f"Could not read crop: {crop_path}")

        # --- geometric normalization --------------------------------------
        # The query and reference crops of the same coin have slightly
        # different rim margins (crop noise). Compare them in a canonical
        # frame: coin centered, fixed radius, residual rotation removed
        # against the best reference.
        qcircle = _detect_coin_circle(query)
        q, total = _canonicalize(query, qcircle)

        # Rotation: align the query to the etalon (best reference) by
        # full-disc NCC in the canonical frame (see _estimate_rotation).
        # All bank references get the same treatment below.
        ref0 = self._first_reference_image(reference_ids, side)
        ref0_canon = None
        if ref0 is not None:
            ref0_canon, _ = _canonicalize(ref0, _detect_coin_circle(ref0))
            ang = _estimate_rotation(q, ref0_canon)
            if abs(ang) > 0.1:
                cq = (q.shape[1] / 2.0, q.shape[0] / 2.0)
                rot = cv2.getRotationMatrix2D(cq, ang, 1.0)
                q = cv2.warpAffine(
                    q, rot, (q.shape[1], q.shape[0]),
                    flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE,
                )
                total = _compose(total, rot)

        # Keep embedding cost bounded: a user-selectable grid (8..128) on a
        # downscaled canvas, then bilinearly upscale the distance map to the
        # requested heatmap size.  The bank MUST be sampled on this same
        # canvas/grid: a query detail (e.g. 64px at full res) is only
        # meaningful against reference patches of the identical scale.
        # The old code fed 256px quadrants as the bank, so no query patch
        # ever found a close match and the distance sat near ~0.7 even for
        # an identical coin (every coin read as an "anomaly").
        grid = int(patch_grid) if patch_grid else 16
        grid = max(4, min(128, grid))
        canvas = max(128, min(1024, grid * 16))

        bank = self._build_memory_bank(reference_ids, side, ref0_canon, grid, canvas)
        if len(bank) == 0:
            # No references: no reliable anomaly estimate.
            return {
                "is_anomaly": False,
                "score": 0.0,
                "heatmap_path": None,
                "heatmap_matrix_path": None,
                "heatmap_size": heatmap_size,
                "bounding_boxes": [],
                "top_patches": [],
                "note": "no references",
            }

        small = cv2.resize(q, (canvas, canvas), interpolation=cv2.INTER_AREA)
        sh, sw = small.shape[:2]
        dist = np.zeros((grid, grid), dtype="float32")
        ph = max(1, sh // grid)
        pw = max(1, sw // grid)
        bank_flat = np.stack(bank, axis=0)  # (M, dim)
        for i in range(grid):
            for j in range(grid):
                patch = small[i * ph : (i + 1) * ph, j * pw : (j + 1) * pw]
                feat = self.embedder.embed_image(patch)
                # nearest bank distance (cosine)
                sims = bank_flat @ feat  # (M,)
                dist[i, j] = 1.0 - float(sims.max())

        # Upscale to the requested heatmap size (raw distances; the peak
        # normalization is done inside the coin disc after the map is back
        # in the original crop coordinates).
        norm = cv2.resize(
            dist, (heatmap_size, heatmap_size), interpolation=cv2.INTER_LINEAR
        ).astype("float32")

        # Map the heatmap back from the canonical frame to the original crop
        # coordinates, so the overlay matches what the user sees on screen.
        # cv2.warpAffine(dst) = src(M^-1 * dst): M must send an ORIGINAL crop
        # point to the downscaled canonical grid, so M = (k*total) and the
        # matrix to pass is its INVERSE. Passing k*total directly (the old
        # bug) sampled the 128px grid at ~8x coordinates and collapsed the
        # whole map into a ~15px blob in the top-left corner.
        h, w = query.shape[:2]
        k = float(heatmap_size) / float(min(w, h))
        norm = cv2.warpAffine(
            norm, cv2.invertAffineTransform(k * total), (w, h),
            flags=cv2.INTER_LINEAR, borderValue=0.0,
        ).astype("float32")

        # Mask the background: patches outside the coin rim have no matching
        # reference, so their distance saturates near 1 and would make one
        # photo corner the only hot spot while the coin reads flat. Zero
        # everything outside the detected coin circle.
        cx, cy, r = qcircle
        disc = np.zeros((h, w), "float32")
        cv2.circle(disc, (int(cx), int(cy)), int(r), 1.0, -1)
        norm_disc = (norm * disc).astype("float32")

        # Score = mean RAW distance inside the coin. A well-matched coin
        # sits near 0; a genuinely different surface pushes it up. (Peak
        # normalization is only for the visual map below — it would
        # otherwise amplify a tiny uniform difference to ~1 and flag every
        # well-matched coin as an anomaly.)
        image_score = float(norm_disc.sum() / float(disc.sum()))
        is_anomaly = image_score >= thr

        # Visual: peak-normalize inside the coin so the strongest surface
        # difference becomes the hottest color (background stays 0/transparent).
        dmax = float(norm_disc.max())
        norm = (norm_disc / dmax).astype("float32") if dmax > 1e-9 else norm_disc

        # Bounding boxes: contiguous regions above threshold
        boxes = self._find_boxes(norm, thr)

        # Persist heatmap
        heat_path = None
        heat_mat_path = None
        norm_path = None
        if coin_id is not None:
            heat_mat = norm.astype("float32")
            heat_mat_path = str(self.storage.heatmap_matrix_path(coin_id, side))
            np.save(heat_mat_path, heat_mat)
            heat_img = self._to_colormap(norm, query.shape, qcircle)
            heat_path = str(self.storage.heatmap_image_path(coin_id, side))
            cv2.imwrite(heat_path, heat_img)
            # Persist the normalized (rotation-aligned) coin so the UI can
            # show the rotated version after comparison.
            norm_path = str(self.storage.normalized_path(coin_id, side))
            cv2.imwrite(norm_path, q)

        # Top anomalous patches (indices in the original crop frame)
        fh, fw = norm.shape
        flat_idx = np.argsort(-norm.ravel())[:8]
        top = []
        for idx in flat_idx:
            i, j = divmod(int(idx), fw)
            top.append({"row": i, "col": j, "distance": round(float(norm[i, j]), 4)})

        return {
            "is_anomaly": is_anomaly,
            "score": round(image_score, 4),
            "heatmap_path": heat_path,
            "heatmap_matrix_path": heat_mat_path,
            "normalized_path": norm_path,
            "heatmap_size": heatmap_size,
            "bounding_boxes": boxes,
            "top_patches": top,
        }

    def _first_reference_image(self, reference_ids: list[int], side: str):
        """First readable reference crop (the best match comes first)."""
        cv2 = _cv2()
        for cid in reference_ids[: self.cfg.num_normal_references]:
            path = self.storage.photo_path(cid, side)
            if not path.exists():
                continue
            img = cv2.imread(str(path), cv2.IMREAD_COLOR)
            if img is not None:
                return img
        return None

    def _build_memory_bank(
        self, reference_ids: list[int], side: str, ref0_canon: "np.ndarray | None" = None,
        grid: int = 16, canvas: int = 256,
    ) -> list[np.ndarray]:
        import cv2  # type: ignore

        bank: list[np.ndarray] = []
        for cid in reference_ids[: self.cfg.num_normal_references]:
            path = self.storage.photo_path(cid, side)
            img = cv2.imread(str(path), cv2.IMREAD_COLOR) if path.exists() else None
            if img is None:
                continue
            # Same canonical frame as the query: coin centered, fixed radius.
            img, _ = _canonicalize(img, _detect_coin_circle(img))
            # Rotate to the etalon (ref0) orientation so every patch is
            # compared in the same rotational frame (full-disc NCC).
            if ref0_canon is not None:
                ang = _estimate_rotation(img, ref0_canon)
                if abs(ang) > 0.1:
                    h, w = img.shape[:2]
                    rot = cv2.getRotationMatrix2D((w / 2.0, h / 2.0), ang, 1.0)
                    img = cv2.warpAffine(
                        img, rot, (w, h),
                        flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE,
                    )
            # Sample the SAME downscaled canvas and grid as the query, so the
            # bank patches are at the identical scale to the query patches.
            # That is what makes a matching coin read ~0 (patch (i,j) here
            # corresponds to patch (i,j) in the query) instead of ~0.7.
            small_ref = cv2.resize(img, (canvas, canvas), interpolation=cv2.INTER_AREA)
            sh, sw = small_ref.shape[:2]
            ph = max(1, sh // grid)
            pw = max(1, sw // grid)
            for yy in range(grid):
                for xx in range(grid):
                    patch = small_ref[yy * ph : (yy + 1) * ph, xx * pw : (xx + 1) * pw]
                    if patch.size == 0:
                        continue
                    bank.append(self.embedder.embed_image(patch))
        return bank

    def _find_boxes(self, norm: np.ndarray, thr: float) -> list[dict]:
        cv2 = _cv2()
        mask = (norm >= thr).astype("uint8") * 255
        mask = cv2.dilate(mask, np.ones((3, 3), np.uint8), iterations=2)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        h, w = norm.shape
        boxes = []
        for c in contours:
            x, y, bw, bh = cv2.boundingRect(c)
            boxes.append(
                {
                    "x": round(x / w, 4),
                    "y": round(y / h, 4),
                    "w": round(bw / w, 4),
                    "h": round(bh / h, 4),
                }
            )
        return boxes

    def _to_colormap(
        self, norm: np.ndarray, shape: tuple[int, int],
        circle: "tuple[float, float, float] | None" = None,
    ) -> "np.ndarray":
        """JET colormap of the distance map, sized to `shape`.

        When the coin circle (cx, cy, r in `shape` pixels) is given, the
        result is BGRA and fully transparent outside the disc, so the
        overlay shows only the coin — not a blue wash over the background.
        """
        cv2 = _cv2()
        heat = (norm * 255).astype("uint8")
        heat = cv2.resize(heat, (shape[1], shape[0]), interpolation=cv2.INTER_LINEAR)
        bgr = cv2.applyColorMap(heat, cv2.COLORMAP_JET)
        if circle is None:
            return bgr
        alpha = np.zeros((shape[0], shape[1]), "uint8")
        cv2.circle(alpha, (int(circle[0]), int(circle[1])), int(circle[2]), 255, -1)
        return cv2.merge([bgr[:, :, 0], bgr[:, :, 1], bgr[:, :, 2], alpha])


def _cv2():
    import cv2  # type: ignore
    return cv2
