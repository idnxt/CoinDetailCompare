"""Anomaly detection.

Production priority: SuperADD (anomalib backbone, ViT-H+/16) with memory bank
and heatmaps. Fallback: AnomalyDINO (training-free, DINOv2/v3 B/L).

Dev fallback implemented here: patch-level cosine distance to a memory bank
built from 1..5 normal reference crops, producing a 64x64 / 128x128 heatmap.
This is a working approximation so the pipeline (heatmap, bbox, UI overlay)
can be exercised end-to-end before the production detector is bound (Phase 4).
"""
from __future__ import annotations

import math
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


def _estimate_rotation(a: "np.ndarray", b: "np.ndarray", coarse_deg: float = 3.0) -> float:
    """Coarse-to-fine angle (deg) whose rotation of `a` best correlates with
    `b`. Both inputs should already be scale/position aligned (canonical)."""
    cv2 = _cv2()
    s = 128
    a = cv2.resize(a, (s, s), interpolation=cv2.INTER_AREA)
    b = cv2.resize(b, (s, s), interpolation=cv2.INTER_AREA)
    gb = cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype("float32").ravel()
    center = (s / 2.0, s / 2.0)

    def score(deg: float) -> float:
        m = cv2.getRotationMatrix2D(center, deg, 1.0)
        ra = cv2.warpAffine(a, m, (s, s), flags=cv2.INTER_LINEAR)
        ra = cv2.cvtColor(ra, cv2.COLOR_BGR2GRAY).astype("float32").ravel()
        c = float(np.corrcoef(ra, gb)[0, 1])
        return c if not math.isnan(c) else -1.0

    best, best_s = 0.0, score(0.0)
    deg = coarse_deg
    while deg < 360.0:
        sc = score(deg)
        if sc > best_s:
            best, best_s = deg, sc
        deg += coarse_deg
    d = best - coarse_deg
    while d <= best + coarse_deg:
        sc = score(d % 360.0)
        if sc > best_s:
            best, best_s = d % 360.0, sc
        d += 0.25
    return best


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
        q, total = _canonicalize(query, _detect_coin_circle(query))

        ref0 = self._first_reference_image(reference_ids, side)
        if ref0 is not None:
            r0, _ = _canonicalize(ref0, _detect_coin_circle(ref0))
            ang = _estimate_rotation(q, r0)
            if abs(ang) > 0.1:
                cq = (q.shape[1] / 2.0, q.shape[0] / 2.0)
                rot = cv2.getRotationMatrix2D(cq, ang, 1.0)
                q = cv2.warpAffine(
                    q, rot, (q.shape[1], q.shape[0]),
                    flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE,
                )
                total = _compose(total, rot)

        bank = self._build_memory_bank(reference_ids, side)
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

        # Keep embedding cost bounded on CPU: a user-selectable grid (8..128)
        # on a downscaled canvas, then bilinearly upscale the distance map
        # to the requested heatmap size.
        grid = int(patch_grid) if patch_grid else 16
        grid = max(4, min(128, grid))
        canvas = max(128, min(1024, grid * 16))
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

        # Normalize to [0, 1], then upscale to the requested heatmap size.
        dmax = float(dist.max()) if dist.size else 0.0
        norm = dist / dmax if dmax > 0 else dist
        norm = cv2.resize(
            norm, (heatmap_size, heatmap_size), interpolation=cv2.INTER_LINEAR
        ).astype("float32")

        # Map the heatmap back from the canonical frame to the original crop
        # coordinates, so the overlay matches what the user sees on screen.
        h, w = query.shape[:2]
        k = float(heatmap_size) / float(min(w, h))
        norm = cv2.warpAffine(
            norm, k * total, (w, h),
            flags=cv2.INTER_LINEAR, borderValue=0.0,
        ).astype("float32")

        image_score = float(np.mean(norm))
        is_anomaly = image_score >= thr

        # Bounding boxes: contiguous regions above threshold
        boxes = self._find_boxes(norm, thr)

        # Persist heatmap
        heat_path = None
        heat_mat_path = None
        if coin_id is not None:
            heat_mat = norm.astype("float32")
            heat_mat_path = str(self.storage.heatmap_matrix_path(coin_id, side))
            np.save(heat_mat_path, heat_mat)
            heat_img = self._to_colormap(norm, query.shape)
            heat_path = str(self.storage.heatmap_image_path(coin_id, side))
            cv2.imwrite(heat_path, heat_img)

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

    def _build_memory_bank(self, reference_ids: list[int], side: str) -> list[np.ndarray]:
        import cv2  # type: ignore

        bank: list[np.ndarray] = []
        for cid in reference_ids[: self.cfg.num_normal_references]:
            path = self.storage.photo_path(cid, side)
            img = cv2.imread(str(path), cv2.IMREAD_COLOR) if path.exists() else None
            if img is None:
                continue
            # Same canonical frame as the query: coin centered, fixed radius.
            img, _ = _canonicalize(img, _detect_coin_circle(img))
            # Sample a few patch features from the reference (4x4 grid:
            # 16 embeddings per reference — keeps CPU cost bounded).
            h, w = img.shape[:2]
            for yy in range(0, h, max(1, h // 4)):
                for xx in range(0, w, max(1, w // 4)):
                    patch = img[yy : yy + max(1, h // 4), xx : xx + max(1, w // 4)]
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

    def _to_colormap(self, norm: np.ndarray, shape: tuple[int, int]) -> "np.ndarray":
        cv2 = _cv2()
        heat = (norm * 255).astype("uint8")
        heat = cv2.resize(heat, (shape[1], shape[0]), interpolation=cv2.INTER_LINEAR)
        return cv2.applyColorMap(heat, cv2.COLORMAP_JET)


def _cv2():
    import cv2  # type: ignore
    return cv2
