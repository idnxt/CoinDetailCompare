"""Smoke test (no heavy deps required): config, storage, mock embedding,
both-sides search. Run:  python tests/smoke_test.py
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "src" / "CoinDetailCompare.Backend"))

import numpy as np  # noqa: E402

from coinbackend.config import Config  # noqa: E402
from coinbackend.embed import MockEmbedder  # noqa: E402
from coinbackend.search import SearchEngine  # noqa: E402
from coinbackend.storage import Storage  # noqa: E402


def _tmp_coimg(tmp: Path) -> Config:
    # Use defaults (not Config.load()) so we don't read the repo config file.
    cfg = Config()
    cfg.host = "127.0.0.1"
    cfg.port = 8300
    cfg.coimg_dir = tmp / "coimg"
    cfg.tmp_dir = tmp / "coimg" / "tmp"
    cfg.db_path = tmp / "coimg" / "catalog.db"
    cfg.schema_path = REPO / "shared" / "sqlite_schema.sql"
    cfg.device = "cpu"
    cfg.model = "vit-l-16"
    cfg.target_size = 1024
    cfg.min_size = 512
    cfg.embedding_dim = 128
    cfg.topk = 5
    cfg.background_color = (200, 200, 200)
    cfg.heatmap_size = 64
    cfg.anomaly_threshold = 0.5
    cfg.num_normal_references = 5
    cfg.numista_api_key = ""
    cfg.numista_base_url = "https://www.numista.com/api/v1"
    for d in (cfg.coimg_dir, cfg.tmp_dir):
        d.mkdir(parents=True, exist_ok=True)
    cfg.db_path.parent.mkdir(parents=True, exist_ok=True)
    return cfg


def main() -> int:
    import tempfile

    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td)
        cfg = _tmp_coimg(tmp)
        storage = Storage(cfg)
        assert storage.cfg.db_path.exists(), "sqlite db was not created"

        emb = MockEmbedder(dim=cfg.embedding_dim)

        # Determinism: same image -> same vector
        a = (np.random.default_rng(0).standard_normal((64, 64, 3)) * 128 + 128).astype("uint8")
        v1 = emb.embed_image(a)
        v2 = emb.embed_image(a)
        assert np.allclose(v1, v2), "mock embedder is not deterministic"
        assert abs(float(np.linalg.norm(v1)) - 1.0) < 1e-4, "vector not unit-normalized"

        # Search engine: add a few coins, verify nearest wins
        eng = SearchEngine(cfg, storage)
        rng = np.random.default_rng(1)

        def put(coin_id: int, side: str, vec: np.ndarray) -> None:
            eng.add_vector(coin_id, side, vec)
            np.save(storage.vector_path(coin_id, side), vec)  # per-coin vector on disk

        # Coin 10 matches the query (same vector), coins 11/12 differ.
        put(10, "obverse", v1)
        put(10, "reverse", v1)
        put(11, "obverse", _l2(rng.standard_normal(cfg.embedding_dim)))
        put(11, "reverse", _l2(rng.standard_normal(cfg.embedding_dim)))
        put(12, "obverse", _l2(rng.standard_normal(cfg.embedding_dim)))
        put(12, "reverse", _l2(rng.standard_normal(cfg.embedding_dim)))
        eng.persist()

        res = eng.search_both_sides(v1, v1, topk=5)
        assert res, "search returned no candidates"
        assert res[0]["coin_id"] == 10, f"expected coin 10 to be top, got {res[0]}"
        assert res[0]["overall_pct"] > 99.0, f"top overall_pct too low: {res[0]['overall_pct']}"
        print("search_both_sides top:", res[0])

        # Rebuild from disk should recover the same set of ids
        counts = eng.rebuild_from_disk()
        assert counts == {"obverse": 3, "reverse": 3}, f"rebuild counts wrong: {counts}"
        res2 = eng.search_both_sides(v1, v1, topk=5)
        assert res2[0]["coin_id"] == 10

    print("SMOKE TEST PASSED")
    return 0


def _l2(v: np.ndarray) -> np.ndarray:
    v = v.astype("float32")
    return v / np.linalg.norm(v)


if __name__ == "__main__":
    raise SystemExit(main())
