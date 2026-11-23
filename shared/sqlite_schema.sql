-- CoinDetailCompare - SQLite schema
-- Shared source of truth for C# (DbService) and Python (storage).
-- ID in FAISS (IndexIDMap) == coin.id

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS coin_type (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  numista_n     TEXT,
  numista_url   TEXT,
  name          TEXT,
  issuer        TEXT,
  period        TEXT,
  type          TEXT,
  years         TEXT,
  calendar      TEXT,
  value         TEXT,
  rate          TEXT,
  currency      TEXT,
  composition   TEXT,
  mass          TEXT,
  diameter      TEXT,
  thickness     TEXT,
  shape         TEXT,
  technique     TEXT,
  orientation   TEXT,
  issued_date   TEXT,
  links         TEXT,
  obverse_text  TEXT,
  reverse_text  TEXT,
  rim_text      TEXT,
  created_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS coin (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,   -- == FAISS vector id
  type_id         INTEGER REFERENCES coin_type(id),
  mint_year       TEXT,
  mint            TEXT,
  quality         TEXT,                                -- G VG F VF XF AU UNC
  price           REAL NOT NULL DEFAULT 0,
  album_name      TEXT,
  page_no         INTEGER,
  order_no        INTEGER,
  obverse_photo   TEXT,
  reverse_photo   TEXT,
  obverse_heatmap TEXT,
  reverse_heatmap TEXT,
  obverse_layer   TEXT,
  reverse_layer   TEXT,
  obverse_vector  TEXT,
  reverse_vector  TEXT,
  is_anomaly      INTEGER NOT NULL DEFAULT 0,
  anomaly_desc    TEXT,
  numista_url     TEXT,
  source_kind     TEXT,                                -- match|duplicate|variant|anomaly|manual|api
  etalon_id       INTEGER REFERENCES coin(id),
  created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_coin_type      ON coin(type_id);
CREATE INDEX IF NOT EXISTS idx_coin_album     ON coin(album_name, page_no, order_no);
CREATE INDEX IF NOT EXISTS idx_coin_quality   ON coin(quality);

CREATE TABLE IF NOT EXISTS reference (
  id      INTEGER PRIMARY KEY AUTOINCREMENT,
  kind    TEXT,   -- quality|rim|orientation|country|denomination|currency
  code    TEXT,
  ru      TEXT,
  en      TEXT
);
CREATE INDEX IF NOT EXISTS idx_ref_kind ON reference(kind);
-- Uniqueness makes reference population (INSERT OR IGNORE) idempotent.
CREATE UNIQUE INDEX IF NOT EXISTS idx_ref_kind_code ON reference(kind, code);

CREATE TABLE IF NOT EXISTS meta (
  key   TEXT PRIMARY KEY,
  value TEXT
);
