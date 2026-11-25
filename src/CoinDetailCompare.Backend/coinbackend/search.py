"""Vector search: two FAISS IndexIDMap indexes (obverse, reverse).

FAISS id == coin.id (hard binding to SQLite).
Fallback: NumPy brute-force with the identical interface when faiss is
unavailable (local dev / CI).
"""
from __future__ import annotations

import numpy as np

from .config import Config
from .storage import Storage

_SIDES = ("obverse", "reverse")


def _faiss():
    try:
        import faiss  # type: ignore
        return faiss
    except Exception:
        return None


def _read_ids(faiss_idx) -> list[int] | None:
    """Read the coin ids stored in an IndexIDMap.

    Returns None when the installed faiss build exposes no id accessor
    (faiss >= 1.16 removed the Python binding of get_ids on IndexIDMap);
    the caller then reconciles ids with the disk (.npy files)."""
    n = int(faiss_idx.ntotal)
    if n <= 0:
        return []
    faiss = _faiss()
    buf = np.empty(n, dtype="int64")
    for candidate in (faiss_idx, faiss.downcast_index(faiss_idx)):
        getter = getattr(candidate, "get_ids", None)
        if getter is None:
            continue
        try:
            getter(buf, 0, n)
            return [int(x) for x in buf.tolist()]
        except Exception:
            continue
    return None


class _Index:
    def __init__(self, dim: int, use_faiss: bool | None = None) -> None:
        self.dim = dim
        self.ids: list[int] = []
        self.matrix: np.ndarray | None = None  # (N, dim) float32, numpy fallback
        self._faiss = None
        self._id_set: set[int] = set()
        if use_faiss is None:
            use_faiss = _faiss() is not None
        if use_faiss:
            faiss = _faiss()
            inner = faiss.IndexFlatIP(dim)  # unit vectors -> inner product == cosine
            self._faiss = faiss.IndexIDMap(inner)

    @property
    def using_faiss(self) -> bool:
        return self._faiss is not None

    def add(self, coin_id: int, vec: np.ndarray) -> None:
        """Insert or replace the vector for `coin_id` (upsert semantics)."""
        vec = np.asarray(vec, dtype="float32").reshape(self.dim)
        if coin_id in self._id_set:
            self._remove(coin_id)
        self._id_set.add(coin_id)
        self.ids.append(coin_id)
        if self._faiss is not None:
            self._faiss.add_with_ids(vec.reshape(1, -1), np.array([coin_id], dtype="int64"))
        else:
            if self.matrix is None:
                self.matrix = vec[None, :]
            else:
                self.matrix = np.vstack([self.matrix, vec[None, :]])

    def _remove(self, coin_id: int) -> None:
        if self._faiss is not None:
            self._faiss.remove_ids(np.array([coin_id], dtype="int64"))
            self._id_set.discard(coin_id)
            return
        if self.matrix is None:
            return
        i = self.ids.index(coin_id)
        self.ids.pop(i)
        self.matrix = np.delete(self.matrix, i, axis=0)
        self._id_set.discard(coin_id)

    def search(self, vec: np.ndarray, topk: int = 10):
        """Return [(coin_id, cosine_score), ...] best first."""
        vec = np.asarray(vec, dtype="float32").reshape(1, self.dim)
        if self._faiss is not None:
            k = min(topk, int(self._faiss.ntotal))
            if k <= 0:
                return []
            scores, idxs = self._faiss.search(vec, k)
            return [(int(i), float(s)) for s, i in zip(scores[0], idxs[0]) if i >= 0]
        return self._brute(vec, topk)

    def _brute(self, vec: np.ndarray, topk: int):
        if self.matrix is None or len(self.ids) == 0:
            return []
        scores = self.matrix @ vec[0]  # unit vectors -> cosine
        order = np.argsort(-scores)[:topk]
        return [(int(self.ids[i]), float(scores[i])) for i in order]

    def save(self, path: str) -> None:
        if self._faiss is not None:
            faiss = _faiss()
            faiss.write_index(self._faiss, path)
        else:
            np.savez(
                path + ".npz",
                ids=np.array(self.ids, dtype="int64"),
                matrix=self.matrix if self.matrix is not None
                else np.zeros((0, self.dim), dtype="float32"),
            )

    def load(self, path: str) -> bool:
        import os
        if os.path.exists(path) and self._faiss is not None:
            faiss = _faiss()
            try:
                loaded = faiss.read_index(path)
                ids = _read_ids(loaded)
                self._faiss = loaded
                # faiss >= 1.16 exposes no Python accessor for IndexIDMap ids:
                # ids may come back empty and are reconciled with the .npy
                # files on disk by SearchEngine._sync_with_disk.
                self.ids = ids if ids is not None else []
                self._id_set = set(self.ids)
                return True
            except Exception:
                self._faiss = None
        npz = path + ".npz"
        if os.path.exists(npz):
            data = np.load(npz)
            self.ids = [int(x) for x in data["ids"].tolist()]
            self._id_set = set(self.ids)
            self.matrix = data["matrix"].astype("float32")
            return True
        return False


class SearchEngine:
    def __init__(self, cfg: Config, storage: Storage) -> None:
        self.cfg = cfg
        self.storage = storage
        self.obverse = _Index(cfg.embedding_dim)
        self.reverse = _Index(cfg.embedding_dim)
        self._try_load()
        self._sync_with_disk()

    def _sync_with_disk(self) -> None:
        """Rebuild from disk if the persisted in-memory index is stale
        (e.g. fewer vectors than .npy files on disk after coins were added)."""
        for side in _SIDES:
            disk_ids = {cid for cid, _ in self.storage.iter_vectors(side)}
            idx = self.obverse if side == "obverse" else self.reverse
            # A faiss index loaded from file may carry no readable ids
            # (faiss >= 1.16): trust the disk ids if the counts match.
            if idx.using_faiss and not idx.ids and disk_ids:
                if int(idx._faiss.ntotal) == len(disk_ids):
                    idx.ids = sorted(disk_ids)
                    idx._id_set = set(disk_ids)
                    continue
            if disk_ids and disk_ids != set(idx.ids):
                self.rebuild_from_disk()
                break

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
                if vec.shape[0] != self.cfg.embedding_dim:
                    continue  # dim mismatch (model change) - skip, will re-embed
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
