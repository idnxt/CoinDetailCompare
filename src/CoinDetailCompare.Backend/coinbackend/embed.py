"""DINOv3 embedding.

Production: DINOv3 ViT-H+/16 (GPU, CUDA 13.3+) or ViT-L/16 & below (CPU).
Dev fallback: a deterministic, low-dim image signature so the pipeline can be
exercised end-to-end without torch/GPU. Clearly NOT a real embedding.
"""
from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np

from .config import Config


class BaseEmbedder:
    dim: int = 0

    def embed_image(self, img: "np.ndarray") -> np.ndarray:  # BGR
        raise NotImplementedError


class MockEmbedder(BaseEmbedder):
    """Deterministic fallback: color+spatial signature -> fixed-size vector.

    Used only when torch/DINOv3 is unavailable (local dev / CI).
    """

    def __init__(self, dim: int = 1024) -> None:
        self.dim = dim

    def embed_image(self, img: "np.ndarray") -> np.ndarray:
        small = _downsample(img, 32)
        h, w, _ = small.shape
        flat = small.reshape(-1)
        # Channel stats + a few spatial bands, hashed into a stable vector.
        features: list[float] = []
        for ch in range(3):
            band = small[:, :, ch].reshape(-1)
            features.extend([float(band.mean()), float(band.std()), float(np.median(band))])
        # spatial bands
        gh, gw = h // 4, w // 4
        for i in range(4):
            for j in range(4):
                block = small[i * gh : (i + 1) * gh, j * gw : (j + 1) * gw].reshape(-1)
                features.append(float(block.mean()))
        features.append(float(small.mean()))
        # Pad/hashed to a deterministic fixed size, unit-normalized.
        digest = hashlib.md5(
            np.round(flat, 3).tobytes() + np.round(np.array(features, dtype="float64"), 3).tobytes()
        ).digest()
        seed = int.from_bytes(digest[:8], "big")
        rng = np.random.default_rng(seed)
        vec = rng.standard_normal(self.dim).astype("float32")
        # Fold the hand-crafted features into the vector (padded to dim), then normalize.
        feats = np.zeros(self.dim, dtype="float32")
        n = min(self.dim, len(features))
        feats[:n] = np.array(features[:n], dtype="float32")
        vec = vec + feats
        vec = _l2(vec)
        return vec


class DinoV3Embedder(BaseEmbedder):
    """Thin wrapper around a DINOv3 model instance."""

    def __init__(self, cfg: Config, model=None) -> None:
        self.cfg = cfg
        self.model = model
        self.dim = cfg.embedding_dim

    def embed_image(self, img: "np.ndarray") -> np.ndarray:
        if self.model is None:
            raise RuntimeError(
                "DINOv3 model is not loaded. Bind a model (device=%s, name=%s)."
                % (self.cfg.device, self.cfg.model)
            )
        tensor = _to_tensor_bgr(img)
        feats = self.model.embed(tensor)  # expected: [1, D]
        vec = np.asarray(feats, dtype="float32").reshape(-1)
        self.dim = vec.shape[0]
        return _l2(vec)


def _downsample(img: "np.ndarray", size: int) -> "np.ndarray":
    try:
        import cv2  # type: ignore
        return cv2.resize(img, (size, size), interpolation=cv2.INTER_AREA)
    except Exception:
        h, w = img.shape[:2]
        step_h = max(1, h // size)
        step_w = max(1, w // size)
        return img[::step_h, ::step_w, :][:size, :size]


def _to_tensor_bgr(img: "np.ndarray"):
    import torch  # type: ignore

    t = torch.from_numpy(np.ascontiguousarray(img[:, :, ::-1])).float()  # BGR->RGB
    t = t / 255.0
    return t.unsqueeze(0).to(device=_device())


def _device():
    import torch  # type: ignore

    return torch.device("cuda" if torch.cuda.is_available() else "cpu")


def _l2(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    if n > 0:
        v = v / n
    return v.astype("float32")


def get_embedder(cfg: Config) -> BaseEmbedder:
    """Return a DINOv3 embedder if the stack is available, else the mock."""
    try:
        import torch  # noqa: F401
    except Exception:
        return MockEmbedder(dim=cfg.embedding_dim)

    model = _load_dinov3(cfg)
    if model is None:
        return MockEmbedder(dim=cfg.embedding_dim)
    return DinoV3Embedder(cfg, model)


def _load_dinov3(cfg: Config):
    """Attempt to build a DINOv3 model from the official repo.

    The exact API depends on the installed dinov3 package; keep this a thin,
    clearly-marked adapter that can be completed once the package is pinned
    (Phase 2). Returns a model object exposing `.embed(tensor) -> [1, D]`.
    """
    try:
        import dinov3  # type: ignore
    except Exception:
        return None
    # TODO(Phase 2): bind dinov3 ViT-{H+,L,B}/16 weights + preprocess per model.
    try:
        name_map = {
            "vit-h-plus-16": "dinov3_vitl16",  # placeholder; map to official model name
            "vit-l-16": "dinov3_vitl16",
            "vit-b-16": "dinov3_vitb16",
        }
        official = name_map.get(cfg.model, "dinov3_vitl16")
        if hasattr(dinov3, "build_model"):
            model = dinov3.build_model(official)
            model = _attach_embed_api(model, cfg)
            return model
    except Exception:
        return None
    return None


def _attach_embed_api(model, cfg: Config):
    """Expose a stable `.embed(tensor)` API on the DINOv3 model."""
    if hasattr(model, "embed"):
        return model

    def embed(tensor):
        with torch.no_grad():  # type: ignore
            out = model.forward_features(tensor) if hasattr(model, "forward_features") else model(tensor)
            if isinstance(out, dict) and "x_norm_cls" in out:
                return out["x_norm_cls"]
            return out
    model.embed = embed
    return model
