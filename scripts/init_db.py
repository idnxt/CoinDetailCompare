"""Initialize the SQLite catalog database from the shared schema.

Usage:  python scripts/init_db.py
Creates the DB at the configured path (config/settings.json or example).
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "src" / "CoinDetailCompare.Backend"))

from coinbackend.config import Config  # noqa: E402
from coinbackend.storage import Storage  # noqa: E402


def main() -> int:
    cfg = Config.load()
    storage = Storage(cfg)
    print(f"Database initialized at: {cfg.db_path}")
    # Verify tables exist.
    with storage.connect() as con:
        rows = con.execute(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
        ).fetchall()
        tables = [r[0] for r in rows]
    print("Tables:", ", ".join(tables))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
