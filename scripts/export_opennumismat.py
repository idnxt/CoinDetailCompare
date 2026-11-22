"""Best-effort export of the CoinDetailCompare catalog to an OpenNumismat .db file.

OpenNumismat stores its collection as a SQLite database (see the demo_*.db files
in OpenNumismat/OpenNumismat/db). This script recreates the same tables and
maps our coin_type/coin rows into `coins`, and the /coimg photos into `photos`.

No OpenNumismat code is copied: only the database structure (table/column names)
is used as a reference. Field mapping is best-effort and documented inline;
field names may need to be refined against a real OpenNumismat file.

Usage:
    python scripts/export_opennumismat.py --out my_collection.db
    python scripts/export_opennumismat.py --db coimg/catalog.db --out out.db --photos coimg
"""
from __future__ import annotations

import argparse
import hashlib
import re
import sqlite3
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_DB = REPO / "coimg" / "catalog.db"
DEFAULT_PHOTOS = REPO / "coimg"

# Tables OpenNumismat expects (structure from its demo .db, no code copied).
SCHEMA = """
CREATE TABLE coins (id INTEGER PRIMARY KEY, title TEXT, value NUMERIC, unit TEXT,
    country TEXT, year INTEGER, period TEXT, mint TEXT, mintmark TEXT, issuedate TEXT,
    type TEXT, series TEXT, subjectshort TEXT, status TEXT, material TEXT, fineness INTEGER,
    shape TEXT, diameter NUMERIC, thickness NUMERIC, weight NUMERIC, grade TEXT,
    edge TEXT, edgelabel TEXT, obvrev TEXT, quality TEXT, mintage INTEGER, dateemis TEXT,
    catalognum1 TEXT, catalognum2 TEXT, catalognum3 TEXT, catalognum4 TEXT, rarity TEXT,
    price1 NUMERIC, price2 NUMERIC, price3 NUMERIC, price4 NUMERIC, variety TEXT,
    obversevar TEXT, reversevar TEXT, edgevar TEXT, note TEXT, image INTEGER,
    obverseimg INTEGER, obversedesign TEXT, obversedesigner TEXT, reverseimg INTEGER,
    reversedesign TEXT, reversedesigner TEXT, edgeimg INTEGER, subject TEXT,
    photo1 INTEGER, photo2 INTEGER, photo3 INTEGER, photo4 INTEGER, defect TEXT,
    storage TEXT, features TEXT, createdat TEXT, updatedat TEXT, quantity INTEGER,
    url TEXT, barcode TEXT, ruler TEXT, region TEXT, obverseengraver TEXT,
    reverseengraver TEXT, obversecolor TEXT, reversecolor TEXT, varietydesc TEXT,
    varietyimg INTEGER, format TEXT, condition TEXT, category TEXT, sort_id INTEGER,
    emitent TEXT, signaturetype TEXT, signature TEXT, signatureimg INTEGER,
    address TEXT, latitude NUMERIC, longitude NUMERIC, photo5 INTEGER, photo6 INTEGER,
    grader TEXT, seat TEXT, native_year TEXT, composition TEXT, material2 TEXT,
    width NUMERIC, height NUMERIC, technique TEXT, modification TEXT, axis INTEGER,
    real_weight NUMERIC, real_diameter NUMERIC, rating TEXT);

CREATE TABLE coins_tags (coin_id INTEGER, tag_id INTEGER);
CREATE TABLE description (id INTEGER PRIMARY KEY, title TEXT, description TEXT,
    author TEXT, icon BLOB);
CREATE TABLE ext_column_settings (table_name TEXT NOT NULL, column_name TEXT NOT NULL,
    title TEXT, enabled INTEGER DEFAULT 1, position INTEGER, width INTEGER DEFAULT 100,
    PRIMARY KEY (table_name, column_name));
CREATE TABLE fields (id INTEGER NOT NULL PRIMARY KEY, title TEXT, enabled INTEGER);
CREATE TABLE images (id INTEGER PRIMARY KEY, image BLOB);
CREATE TABLE pages (id INTEGER PRIMARY KEY, title TEXT, isopen INTEGER, position INTEGER,
    type INTEGER, icon BLOB);
CREATE TABLE photos (id INTEGER PRIMARY KEY, title TEXT, image BLOB, author TEXT,
    license TEXT, source TEXT, phash INTEGER);
CREATE TABLE prices (id INTEGER NOT NULL PRIMARY KEY, coin_id INTEGER, action TEXT,
    date TEXT, quantity INTEGER, price NUMERIC, currency TEXT, total_price NUMERIC,
    shipping NUMERIC, grade TEXT, url TEXT, place TEXT, number TEXT, counterparty TEXT,
    info TEXT, start_bid NUMERIC, position INTEGER);
CREATE TABLE settings (title CHAR NOT NULL UNIQUE, value CHAR);
CREATE TABLE tags (id INTEGER NOT NULL PRIMARY KEY, tag TEXT, parent_id INTEGER,
    position INTEGER, description TEXT, icon BLOB);
"""

# Minimal `fields` table content mirroring the demo file (column visibility).
FIELDS = [
    (0, "ID", 0),
    (1, "Title", 1),
    (2, "Value", 1),
    (3, "Unit", 1),
    (4, "Country", 1),
    (5, "Year", 1),
    (6, "Period", 1),
    (7, "Mint", 1),
    (8, "Series", 1),
    (9, "Status", 1),
    (10, "Material", 1),
    (11, "Grade", 1),
    (12, "ObvRev", 1),
    (13, "Quantity", 1),
    (14, "URL", 1),
]

SETTINGS = [
    ("Version", "11"),
    ("Type", "OpenNumismat"),
    ("Password", hashlib.md5(b"").hexdigest()),  # empty password (same as demo)
    ("ImageSideLen", "0"),
    ("image_height", "1.5"),
    ("free_numeric", "False"),
    ("convert_fraction", "False"),
    ("images_at_bottom", "False"),
]


def _num(value):
    """Best-effort numeric parse ('21.25' -> 21.25, '1991' -> 1991, else None)."""
    if value is None:
        return None
    m = re.match(r"^\s*[-+]?\d+(?:\.\d+)?\s*$", str(value))
    if not m:
        return None
    text = str(value).strip()
    return int(text) if "." not in text else float(text)


def export(src_db: Path, photos_dir: Path, out_db: Path) -> dict:
    if not src_db.exists():
        raise SystemExit(f"source db not found: {src_db}")
    if out_db.exists():
        out_db.unlink()
    out = sqlite3.connect(str(out_db))
    out.executescript(SCHEMA)
    out.executemany("INSERT INTO settings (title, value) VALUES (?, ?)", SETTINGS)
    out.executemany("INSERT INTO fields (id, title, enabled) VALUES (?, ?, ?)", FIELDS)

    src = sqlite3.connect(str(src_db))
    src.row_factory = sqlite3.Row
    types = {row["id"]: dict(row) for row in src.execute("SELECT * FROM coin_type")}
    coins = src.execute("SELECT * FROM coin ORDER BY id").fetchall()

    exported = 0
    photo_count = 0
    for coin in coins:
        t = types.get(coin["type_id"]) or {}

        def tcol(name):
            return t.get(name)

        notes = []
        if coin["album_name"]:
            album = coin["album_name"]
            page = coin["page_no"]
            order = coin["order_no"]
            notes.append(f"{album} (page {page}, order {order})" if page is not None else album)
        if coin["anomaly_desc"]:
            notes.append(f"Anomaly: {coin['anomaly_desc']}")
        if coin["source_kind"] and coin["source_kind"] != "manual":
            notes.append(f"source: {coin['source_kind']}")
            if coin["etalon_id"] is not None:
                notes.append(f"etalon coin #{coin['etalon_id']}")

        row = {
            "id": coin["id"],
            "title": tcol("name") or f"Coin #{coin['id']}",
            "value": _num(tcol("value")),
            "unit": tcol("currency"),
            # We do not store country separately; issuer is the closest match.
            "country": tcol("issuer"),
            "emitent": tcol("issuer"),
            "year": _num(coin["mint_year"]),
            "native_year": coin["mint_year"],
            "period": tcol("period"),
            "mint": coin["mint"],
            "type": tcol("type"),
            "series": tcol("name"),
            "status": "Coin",
            "material": tcol("composition"),
            "composition": tcol("composition"),
            "shape": tcol("shape"),
            "diameter": _num(tcol("diameter")),
            "thickness": _num(tcol("thickness")),
            "weight": _num(tcol("mass")),
            "technique": tcol("technique"),
            "grade": coin["quality"],
            "obvrev": tcol("orientation"),
            "axis": _num(tcol("orientation")),
            "quality": coin["anomaly_desc"],
            "defect": coin["anomaly_desc"],
            "note": "; ".join(notes) or None,
            "quantity": 1,
            "url": coin["numista_url"],
            "createdat": coin["created_at"],
            "updatedat": coin["created_at"],
        }
        if coin["price"]:
            row["price1"] = coin["price"]

        # Photos: obverse -> photo1, reverse -> photo2 (BLOB in `photos`).
        for col, side in (("photo1", "obverse"), ("photo2", "reverse")):
            rel = coin[f"{side}_photo"]
            if rel:
                p = (photos_dir / Path(rel).name) if not Path(rel).is_absolute() else Path(rel)
                if p.exists():
                    photo_id = out.execute(
                        "INSERT INTO photos (title, image) VALUES (?, ?)",
                        (f"coin {coin['id']} {side}", p.read_bytes()),
                    ).lastrowid
                    row[col] = photo_id
                    photo_count += 1

        out.execute(
            f"INSERT INTO coins ({', '.join(row)}) VALUES ({', '.join('?' * len(row))})",
            list(row.values()),
        )
        exported += 1

    src.close()
    out.commit()
    out.close()
    return {"coins": exported, "photos": photo_count, "out": str(out_db)}


def main() -> int:
    ap = argparse.ArgumentParser(description="Export catalog to OpenNumismat .db (best-effort).")
    ap.add_argument("--db", type=Path, default=DEFAULT_DB, help="source catalog.db")
    ap.add_argument("--photos", type=Path, default=DEFAULT_PHOTOS, help="/coimg photos dir")
    ap.add_argument("--out", type=Path, required=True, help="destination .db file")
    args = ap.parse_args()
    stats = export(args.db, args.photos, args.out)
    print(f"exported {stats['coins']} coins, {stats['photos']} photos -> {stats['out']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
