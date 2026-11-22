"""DINOv3 embedding.

Production: DINOv3 ViT-H+/16 (GPU, CUDA 13.3+) with fallback to ViT-L/16 &
below on CPU. Loading order:
  1. local weights (config vision.weights_dir) if present,
  2. torch.hub (facebookresearch/dinov3, then DINOv2 as a last resort),
  3. Hugging Face transformers (ViT family).
If torch is unavailable entirely -> MockEmbedder (deterministic dev fallback,
clearly NOT a real embedding).
"""
from __future__ import annotations

from pathlib import Path

import numpy as np

from .config import Config

# ImageNet normalization (standard for DINOv2/v3-style ViTs)
_MEAN = (0.485, 0.456, 0.406)
_STD = (0.229, 0.224, 0.225)

# Candidate model names per configured key, in preference order.
# Pin the exact official names on the target machine (Phase 2, real tests).
_HUB_REPOS = ("facebookresearch/dinov3", "facebookresearch/dinov2")
_HUB_NAMES = {
    "vit-h-plus-16": ["dinov3_vitl16", "dinov2_vitl14_reg"],
    "vit-l-16": ["dinov3_vitl16", "dinov2_vitl14_reg"],
    "vit-b-16": ["dinov3_vitb16", "dinov2_vitb14_reg"],
}
_HF_REPOS = {
    "vit-h-plus-16": ["facebook/dinov2-vitl14-reg", "facebook/dinov2-vitl14"],
    "vit-l-16": ["facebook/dinov2-vitl14-reg", "facebook/dinov2-vitl14"],
    "vit-b-16": ["facebook/dinov2-vitb14-reg", "facebook/dinov2-vitb14"],
}


class BaseEmbedder:
    dim: int = 0
    name: str = "base"

    def embed_image(self, img: "np.ndarray") -> np.ndarray:  # BGR
        raise NotImplementedError


class MockEmbedder(BaseEmbedder):
    """Deterministic fallback: color+spatial signature -> fixed-size vector.

    Used only when torch/DINOv3 is unavailable (local dev / CI).
    """

    name = "mock"

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
        digest = __import__("hashlib").md5(
            np.round(flat, 3).tobytes()
            + np.round(np.array(features, dtype="float64"), 3).tobytes()
        ).digest()
        seed = int.from_bytes(digest[:8], "big")
        rng = np.random.default_rng(seed)
        vec = rng.standard_normal(self.dim).astype("float32")
        # Fold the hand-crafted features into the vector (padded to dim), then normalize.
        feats = np.zeros(self.dim, dtype="float32")
        n = min(self.dim, len(features))
        feats[:n] = np.array(features[:n], dtype="float32")
        vec = vec + feats
        return _l2(vec)


class DinoV3Embedder(BaseEmbedder):
    """Wrapper exposing a uniform embed_image() over a loaded DINOv3/v2 model."""

    def __init__(self, cfg: Config, model: "_DinoModel", name: str) -> None:
        self.cfg = cfg
        self.model = model
        self.dim = model.dim
        self.name = name

    def embed_image(self, img: "np.ndarray") -> np.ndarray:
        tensor = _to_tensor_bgr(img, self.model.device)
        feats = self.model.embed(tensor)  # [1, D]
        vec = np.asarray(feats, dtype="float32").reshape(-1)
        return _l2(vec)


class _DinoModel:
    """Uniform forward API: resize -> normalize -> forward -> CLS -> L2."""

    def __init__(self, raw, device, input_size: int) -> None:
        self.raw = raw
        self.device = device
        self.input_size = input_size
        self._dim: int | None = None
        raw.eval()
        raw.to(device)

    @property
    def dim(self) -> int:
        if self._dim is None:
            self._probe()
        return self._dim  # type: ignore[return-value]

    def _probe(self) -> None:
        import torch  # type: ignore

        # DINOv2/v3 require the input to be a multiple of the patch size (14).
        size = self.input_size
        if size % 14:
            size = 14 * ((size // 14) + 1)
        with torch.no_grad():
            dummy = torch.zeros(1, 3, size, size, device=self.device)
            out = self._forward(dummy)
        self._dim = int(out.shape[-1])

    def _forward(self, x):
        import torch  # type: ignore

        out = self.raw(x)
        # transformers ViT: .last_hidden_state [B, N+1, D]; hub DINOv2/v3: [B, D]
        if hasattr(out, "last_hidden_state"):
            return out.last_hidden_state[:, 0, :]  # CLS
        if isinstance(out, dict):
            if "x_norm_cls" in out:
                return out["x_norm_cls"]
            return out["cls"]
        out = out[0] if isinstance(out, tuple) else out
        if out.dim() == 3:
            return out[:, 0, :]
        return out

    def embed(self, tensor) -> "object":
        import torch  # type: ignore
        from torch.nn import functional as F  # type: ignore

        x = F.interpolate(tensor, size=(self.input_size, self.input_size),
                          mode="bilinear", align_corners=False)
        mean = torch.tensor(_MEAN, device=x.device).view(1, 3, 1, 1)
        std = torch.tensor(_STD, device=x.device).view(1, 3, 1, 1)
        x = (x - mean) / std
        with torch.no_grad():
            out = self._forward(x)
        return F.normalize(out, dim=-1)


def _device(cfg: Config):
    """Pick cuda if it *actually* works, else cpu.

    torch.cuda.is_available() can be True even when the installed PyTorch
    build has no kernels for the GPU's compute capability (e.g. sm_120 /
    Blackwell needs a CUDA 12.8+/13 build). Verify with a tiny op.
    """
    import torch  # type: ignore

    if cfg.device == "gpu" and torch.cuda.is_available():
        try:
            x = torch.zeros(1, device="cuda")
            _ = x + 1
            return torch.device("cuda")
        except Exception:
            return torch.device("cpu")
    return torch.device("cpu")


def _load_dinov3(cfg: Config):
    """Load DINOv3 (or fallback) weights. Returns _DinoModel or None.

    Order: local file -> torch.hub candidates -> Hugging Face candidates.
    """
    import torch  # type: ignore

    device = _device(cfg)
    input_size = int(getattr(cfg, "embed_input_size", 224))
    key = cfg.model if cfg.model in _HUB_NAMES else "vit-l-16"

    # 1) local weights (state_dict .bin/.pt) under weights_dir
    weights_dir = Path(getattr(cfg, "weights_dir", Path(cfg.coimg_dir) / "models"))
    local = sorted(weights_dir.glob(f"*{key}*")) if weights_dir.exists() else []
    if local:
        try:
            sd = torch.load(str(local[0]), map_location=device)
            model = _build_hub_model(key, local[0].stem, pretrained=False)
            if model is not None:
                model.load_state_dict(sd)
                return _DinoModel(model, device, input_size)
        except Exception:
            pass  # fall through to hub/HF

    # 2) torch.hub
    for repo in _HUB_REPOS:
        for name in _HUB_NAMES[key]:
            try:
                model = torch.hub.load(repo, name, pretrained=True, trust_repo=True)
                return _DinoModel(model, device, input_size)
            except Exception:
                continue

    # 3) Hugging Face transformers
    try:
        from transformers import AutoModel  # type: ignore
        for repo in _HF_REPOS[key]:
            try:
                model = AutoModel.from_pretrained(repo)
                return _DinoModel(model, device, input_size)
            except Exception:
                continue
    except Exception:
        pass

    return None


def _build_hub_model(key: str, weight_name: str, pretrained: bool):
    """Build an *unloaded* hub model architecture (for local state_dict)."""
    import torch  # type: ignore

    for repo in _HUB_REPOS:
        for name in _HUB_NAMES[key]:
            try:
                return torch.hub.load(repo, name, pretrained=pretrained, trust_repo=True)
            except Exception:
                continue
    return None


def _downsample(img: "np.ndarray", size: int) -> "np.ndarray":
    try:
        import cv2  # type: ignore
        return cv2.resize(img, (size, size), interpolation=cv2.INTER_AREA)
    except Exception:
        h, w = img.shape[:2]
        step_h = max(1, h // size)
        step_w = max(1, w // size)
        return img[::step_h, ::step_w, :][:size, :size]


def _to_tensor_bgr(img: "np.ndarray", device) -> "object":
    import torch  # type: ignore

    t = torch.from_numpy(np.ascontiguousarray(img[:, :, ::-1])).float()  # BGR->RGB
    t = t / 255.0
    return t.permute(2, 0, 1).unsqueeze(0).contiguous().to(device)  # [1, 3, H, W]


def _l2(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    if n > 0:
        v = v / n
    return v.astype("float32")


def get_embedder(cfg: Config) -> BaseEmbedder:
    """Return a DINOv3 embedder if the stack is available, else the mock.

    Updates cfg.embedding_dim to the real model dimension when loaded.
    """
    try:
        import torch  # noqa: F401  # type: ignore
    except Exception:
        return MockEmbedder(dim=cfg.embedding_dim)

    model = _load_dinov3(cfg)
    if model is None:
        return MockEmbedder(dim=cfg.embedding_dim)
    try:
        dim = model.dim  # probe (can fail if the model can't run on the device)
    except Exception:
        return MockEmbedder(dim=cfg.embedding_dim)
    cfg.embedding_dim = dim
    return DinoV3Embedder(cfg, model, name=cfg.model)
