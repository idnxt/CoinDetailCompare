"""Disk + SQLite storage for /coimg and the catalog database.

File naming convention (see docs/DB_SCHEMA.md):
    /coimg/{id}_{side}.png            photo
    /coimg/{id}_{side}.npy            DINOv3 vector
    /coimg/{id}_{side}_heatmap.png    heatmap visualization
    /coimg/{id}_{side}_heatmap.npy    heatmap matrix (64x64 / 128x128)
    /coimg/{id}_{side}_layer.json     user vector layer (normalized 0.0..1.0)
"""
from __future__ import annotations

import json
import sqlite3
from pathlib import Path

from .config import Config

SIDES = ("obverse", "reverse")


class Storage:
    def __init__(self, cfg: Config) -> None:
        self.cfg = cfg
        self._init_db()

    # ---- database ----
    def _init_db(self) -> None:
        schema = self.cfg.schema_path.read_text(encoding="utf-8")
        with sqlite3.connect(self.cfg.db_path) as con:
            con.executescript(schema)
            con.commit()
        self.populate_references()

    # ---- reference lists (shared/references/*.json -> reference table) ----
    def populate_references(self) -> int:
        """Load shared/references/*.json into the reference table (idempotent)."""
        refs_dir = self.cfg.references_dir
        if not refs_dir.exists():
            return 0
        inserted = 0
        with sqlite3.connect(self.cfg.db_path) as con:
            for f in sorted(refs_dir.glob("*.json")):
                items = json.loads(f.read_text(encoding="utf-8"))
                for it in items:
                    cur = con.execute(
                        "INSERT OR IGNORE INTO reference (kind, code, ru, en) VALUES (?,?,?,?)",
                        (it.get("kind"), it.get("code"), it.get("ru"), it.get("en")),
                    )
                    inserted += cur.rowcount
            con.commit()
        return inserted

    def list_references(self, kind: str | None = None) -> list[dict]:
        with sqlite3.connect(self.cfg.db_path) as con:
            con.row_factory = sqlite3.Row
            if kind:
                rows = con.execute(
                    "SELECT kind, code, ru, en FROM reference WHERE kind=? ORDER BY code", (kind,)
                )
            else:
                rows = con.execute("SELECT kind, code, ru, en FROM reference ORDER BY kind, code")
            return [dict(r) for r in rows.fetchall()]

    def connect(self) -> sqlite3.Connection:
        con = sqlite3.connect(self.cfg.db_path)
        con.row_factory = sqlite3.Row
        return con

    def next_coin_id(self) -> int:
        with sqlite3.connect(self.cfg.db_path) as con:
            cur = con.execute("SELECT MAX(id) FROM coin")
            row = cur.fetchone()
            cur.execute("INSERT INTO coin (mint_year) VALUES (NULL) RETURNING id")
            new_id = cur.fetchone()[0]
            con.commit()
        return new_id

    # ---- coin deletion ----
    def coin_files(self, coin_id: int) -> list[Path]:
        """Every per-coin file that may exist on disk (both sides)."""
        paths = []
        for side in SIDES:
            paths += [
                self.photo_path(coin_id, side),
                self.vector_path(coin_id, side),
                self.heatmap_image_path(coin_id, side),
                self.heatmap_matrix_path(coin_id, side),
                self.normalized_path(coin_id, side),
                self.layer_path(coin_id, side),
                self.cfg.coimg_dir / f"{coin_id}_{side}_diff.png",
            ]
        return paths

    def delete_coin(self, coin_id: int) -> dict:
        """Delete the coin row and all of its files.

        Coins that pointed at this coin as their etalon (duplicates/
        variants/anomalies) are detached: etalon_id -> NULL, so they stay
        in the catalog as ordinary coins. Returns the delete summary."""
        with sqlite3.connect(self.cfg.db_path) as con:
            cur = con.execute("UPDATE coin SET etalon_id = NULL WHERE etalon_id = ?", (coin_id,))
            detached = cur.rowcount
            con.execute("DELETE FROM coin WHERE id = ?", (coin_id,))
            con.commit()
        removed = 0
        for path in self.coin_files(coin_id):
            try:
                path.unlink()
                removed += 1
            except FileNotFoundError:
                pass
        return {"detached": detached, "files_removed": removed}

    # ---- paths ----
    def photo_path(self, coin_id: int, side: str) -> Path:
        return self.cfg.coimg_dir / f"{coin_id}_{side}.png"

    def vector_path(self, coin_id: int, side: str) -> Path:
        return self.cfg.coimg_dir / f"{coin_id}_{side}.npy"

    def heatmap_image_path(self, coin_id: int, side: str) -> Path:
        return self.cfg.coimg_dir / f"{coin_id}_{side}_heatmap.png"

    def normalized_path(self, coin_id: int, side: str) -> Path:
        """Canonical-frame image: coin centered, rotation normalized to the etalon."""
        return self.cfg.coimg_dir / f"{coin_id}_{side}_norm.png"

    def heatmap_matrix_path(self, coin_id: int, side: str) -> Path:
        return self.cfg.coimg_dir / f"{coin_id}_{side}_heatmap.npy"

    def layer_path(self, coin_id: int, side: str) -> Path:
        return self.cfg.coimg_dir / f"{coin_id}_{side}_layer.json"

    def tmp_path(self, name: str) -> Path:
        return self.cfg.tmp_dir / name

    # ---- user layer (vector primitives in normalized coordinates) ----
    def save_layer(self, coin_id: int, side: str, layer: dict) -> Path:
        path = self.layer_path(coin_id, side)
        path.write_text(json.dumps(layer, ensure_ascii=False, indent=2), encoding="utf-8")
        return path

    def load_layer(self, coin_id: int, side: str) -> dict | None:
        path = self.layer_path(coin_id, side)
        if not path.exists():
            return None
        return json.loads(path.read_text(encoding="utf-8"))

    # ---- vector index files on disk ----
    def index_files(self) -> tuple[Path, Path]:
        return (
            self.cfg.coimg_dir / "obverse.faiss",
            self.cfg.coimg_dir / "reverse.faiss",
        )

    def iter_vectors(self, side: str):
        """Yield (coin_id, vector_path) for coins that have a vector for `side`."""
        for p in self.cfg.coimg_dir.glob(f"*_{side}.npy"):
            stem = p.name.rsplit("_", 1)[0]
            coin_id = int(stem)
            yield coin_id, p
