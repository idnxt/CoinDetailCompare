"""Vector search: two FAISS IndexIDMap indexes (obverse, reverse).

Fallback: NumPy brute-force with identical interface when faiss is unavailable.
FAISS id == coin.id (hard binding to SQLite).
"""
from __future__ import annotations

import numpy as np

from .config import Config
from .storage import Storage

_SIDES = ("obverse", "reverse")


class _Index:
    def __init__(self, dim: int, id_map: bool = True) -> None:
        self.dim = dim
        self.id_map = id_map
        self.ids: list[int] = []
        self.matrix: np.ndarray | None = None  # (N, dim) float32
        self._faiss = None

    def add(self, coin_id: int, vec: np.ndarray) -> None:
        vec = np.asarray(vec, dtype="float32").reshape(self.dim)
        if self.id_map and self._faiss is not None:
            self._faiss.add_with_ids(vec.reshape(1, -1), np.array([coin_id], dtype="int64"))
        self.ids.append(coin_id)
        if self.matrix is None:
            self.matrix = vec[None, :]
        else:
            self.matrix = np.vstack([self.matrix, vec[None, :]])

    def search(self, vec: np.ndarray, topk: int = 10):
        vec = np.asarray(vec, dtype="float32").reshape(1, self.dim)
        if self._faiss is not None and self.id_map:
            scores, idxs = self._faiss.search(vec, topk)
            out = []
            for s, i in zip(scores[0], idxs[0]):
                if i >= 0:
                    out.append((int(i), float(s)))
            return out
        return self._brute(vec, topk)

    def _brute(self, vec: np.ndarray, topk: int):
        if self.matrix is None or len(self.ids) == 0:
            return []
        scores = self.matrix @ vec[0]  # unit vectors -> cosine
        order = np.argsort(-scores)[:topk]
        return [(int(self.ids[i]), float(scores[i])) for i in order]

    def save(self, path: str) -> None:
        if self._faiss is not None and self.id_map:
            import faiss  # type: ignore
            faiss.write_index(self._faiss, path)
        else:
            np.savez(
                path + ".npz",
                ids=np.array(self.ids, dtype="int64"),
                matrix=self.matrix if self.matrix is not None else np.zeros((0, self.dim), dtype="float32"),
            )

    def load(self, path: str) -> bool:
        if path.endswith(".faiss"):
            try:
                import faiss  # type: ignore
                self._faiss = faiss.read_index(path)
                self.ids = []
                self.matrix = None
                return True
            except Exception:
                return False
        npz = path + ".npz"
        if npz.endswith(".faiss.npz"):
            pass
        import os
        if os.path.exists(path + ".npz"):
            data = np.load(path + ".npz")
            self.ids = [int(x) for x in data["ids"].tolist()]
            self.matrix = data["matrix"].astype("float32")
            self._faiss = None
            return True
        return False


class SearchEngine:
    def __init__(self, cfg: Config, storage: Storage) -> None:
        self.cfg = cfg
        self.storage = storage
        self.obverse = _Index(cfg.embedding_dim)
        self.reverse = _Index(cfg.embedding_dim)
        self._try_load()

    def _try_load(self) -> None:
        paths = self.storage.index_files()
        self.obverse.load(str(paths[0]))
        self.reverse.load(str(paths[1]))

    def add_vector(self, coin_id: int, side: str, vec: np.ndarray) -> None:
        idx = self.obverse if side == "obverse" else self.reverse
        idx.add(coin_id, vec)

    def persist(self) -> None:
        paths = self.storage.index_files()
        self.obverse.save(str(paths[0]))
        self.reverse.save(str(paths[1]))

    def rebuild_from_disk(self) -> dict:
        """Rebuild both indexes from .npy vectors on disk (button/background task)."""
        self.obverse = _Index(self.cfg.embedding_dim)
        self.reverse = _Index(self.cfg.embedding_dim)
        counts = {"obverse": 0, "reverse": 0}
        for side in _SIDES:
            idx = self.obverse if side == "obverse" else self.reverse
            for coin_id, vpath in self.storage.iter_vectors(side):
                vec = np.load(vpath)
                idx.add(coin_id, vec)
                counts[side] += 1
        self.persist()
        return counts

    def search_side(self, side: str, vec: np.ndarray, topk: int = 10):
        idx = self.obverse if side == "obverse" else self.reverse
        return idx.search(vec, topk)

    def search_both_sides(self, obverse_vec: np.ndarray, reverse_vec: np.ndarray, topk: int = 10):
        """Search checking both side orientations (A-A+R-R and A-R+R-A).

        Returns a list of candidate dicts sorted by best overall score.
        """
        ov = self.obverse.search(obverse_vec, topk)
        rv = self.reverse.search(reverse_vec, topk)
        # Cross-orientation queries (independent of candidate): A->R and R->A
        a_to_r = dict(self.reverse.search(obverse_vec, topk))
        r_to_a = dict(self.obverse.search(reverse_vec, topk))

        # Map id -> best per-side score
        ov_score = {cid: s for cid, s in ov}
        rv_score = {cid: s for cid, s in rv}
        ids = sorted(set(ov_score) | set(rv_score))

        candidates = []
        for cid in ids:
            # Orientation 1: aligned (A-A + R-R)
            s_aligned = (ov_score.get(cid, 0.0) + rv_score.get(cid, 0.0)) / 2.0
            # Orientation 2: swapped (A-R + R-A)
            s_swapped = (a_to_r.get(cid, 0.0) + r_to_a.get(cid, 0.0)) / 2.0

            if s_swapped > s_aligned:
                orientation = "A-R+R-A"
                overall = s_swapped
            else:
                orientation = "A-A+R-R"
                overall = s_aligned

            candidates.append(
                {
                    "coin_id": cid,
                    "obverse_pct": _pct(ov_score.get(cid, 0.0)),
                    "reverse_pct": _pct(rv_score.get(cid, 0.0)),
                    "overall_pct": _pct(overall),
                    "orientation": orientation,
                }
            )
        candidates.sort(key=lambda c: c["overall_pct"], reverse=True)
        return candidates[:topk]


def _pct(score: float) -> float:
    # cosine in [-1, 1] -> percent in [0, 100]; clamp negatives to 0
    return max(0.0, min(100.0, float(score) * 100.0))
