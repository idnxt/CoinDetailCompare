"""Backend configuration.

Loaded from an optional JSON file (config/settings.json) with sane defaults.
The C# app points at this service via host/port in its own settings.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path


def _repo_root() -> Path:
    # <repo>/src/CoinDetailCompare.Backend/coinbackend/config.py
    # parents: [0]=coinbackend [1]=CoinDetailCompare.Backend [2]=src [3]=<repo>
    here = Path(__file__).resolve()
    return here.parents[3]


@dataclass
class Config:
    # Network
    host: str = "127.0.0.1"
    port: int = 8300

    # Storage
    coimg_dir: Path = field(default_factory=lambda: _repo_root() / "coimg")
    tmp_dir: Path = field(default_factory=lambda: _repo_root() / "coimg" / "tmp")
    db_path: Path = field(default_factory=lambda: _repo_root() / "coimg" / "catalog.db")
    schema_path: Path = field(
        default_factory=lambda: _repo_root() / "shared" / "sqlite_schema.sql"
    )
    references_dir: Path = field(
        default_factory=lambda: _repo_root() / "shared" / "references"
    )

    # Vision
    device: str = "gpu"  # "gpu" | "cpu"
    model: str = "vit-h-plus-16"  # vit-h-plus-16 | vit-l-16 | vit-b-16
    target_size: int = 1024
    min_size: int = 512
    embedding_dim: int = 1024  # DINOv3 ViT-H+ CLS dim (updated when model loads)
    embed_input_size: int = 224  # ViT input resolution
    weights_dir: Path = field(default_factory=lambda: _repo_root() / "coimg" / "models")

    # Search
    topk: int = 10
    background_color: tuple[int, int, int] = (200, 200, 200)  # neutral grey

    # Anomaly
    heatmap_size: int = 128  # 64 | 128
    anomaly_threshold: float = 0.5
    anomaly_backend: str = "auto"  # auto | superadd | anomalydino | patch
    num_normal_references: int = 5  # 1..5

    # Numista API
    numista_api_key: str = ""
    numista_base_url: str = "https://www.numista.com/api/v1"
    numista_search_path: str = "/search"

    @classmethod
    def load(cls, path: str | Path | None = None) -> "Config":
        cfg = cls()
        candidates = [
            Path(path) if path else None,
            _repo_root() / "config" / "settings.json",
            _repo_root() / "config" / "settings.example.json",
        ]
        data: dict = {}
        for cand in candidates:
            if cand and Path(cand).exists():
                with open(cand, "r", encoding="utf-8") as fh:
                    data = json.load(fh)
                break
        cfg = _apply(cfg, data)
        _ensure_dirs(cfg)
        return cfg


def _apply(cfg: Config, data: dict) -> Config:
    if not data:
        return cfg
    backend = data.get("backend", {})
    storage = data.get("storage", {})
    vision = data.get("vision", {})
    search = data.get("search", {})
    anomaly = data.get("anomaly", {})
    numista = data.get("numista", {})

    cfg.host = backend.get("host", cfg.host)
    cfg.port = int(backend.get("port", cfg.port))

    cfg.coimg_dir = Path(storage.get("coimg_dir", cfg.coimg_dir))
    cfg.tmp_dir = Path(storage.get("tmp_dir", cfg.tmp_dir))
    cfg.db_path = Path(storage.get("db_path", cfg.db_path))

    cfg.device = vision.get("device", cfg.device)
    cfg.model = vision.get("model", cfg.model)
    cfg.target_size = int(vision.get("target_size", cfg.target_size))
    cfg.min_size = int(vision.get("min_size", cfg.min_size))
    cfg.embedding_dim = int(vision.get("embedding_dim", cfg.embedding_dim))
    cfg.embed_input_size = int(vision.get("embed_input_size", cfg.embed_input_size))
    cfg.weights_dir = Path(vision.get("weights_dir", cfg.weights_dir))
    cfg.topk = int(search.get("topk", cfg.topk))
    if "background_color" in search:
        cfg.background_color = tuple(int(x) for x in search["background_color"])

    cfg.heatmap_size = int(anomaly.get("heatmap_size", cfg.heatmap_size))
    cfg.anomaly_threshold = float(anomaly.get("threshold", cfg.anomaly_threshold))
    cfg.anomaly_backend = str(anomaly.get("backend", cfg.anomaly_backend)).lower()
    cfg.num_normal_references = int(anomaly.get("num_normal_references", cfg.num_normal_references))

    cfg.numista_api_key = numista.get("api_key", cfg.numista_api_key)
    cfg.numista_base_url = numista.get("base_url", cfg.numista_base_url)
    cfg.numista_search_path = numista.get("search_path", cfg.numista_search_path)
    return cfg


def _ensure_dirs(cfg: Config) -> None:
    for d in (cfg.coimg_dir, cfg.tmp_dir):
        d.mkdir(parents=True, exist_ok=True)
    cfg.db_path.parent.mkdir(parents=True, exist_ok=True)
