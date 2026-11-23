"""One-time: dedupe reference table + add unique index + drop empty coins."""
import sqlite3

DB = r"E:\CoinDetailCompare\coimg\catalog.db"
con = sqlite3.connect(DB)

before = con.execute("SELECT COUNT(*) FROM reference").fetchone()[0]
con.execute("DELETE FROM reference WHERE id NOT IN (SELECT MIN(id) FROM reference GROUP BY kind, code)")
con.execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_ref_kind_code ON reference(kind, code)")

coins_before = con.execute("SELECT COUNT(*) FROM coin").fetchone()[0]
con.execute("""DELETE FROM coin WHERE source_kind IS NULL
               AND obverse_photo IS NULL AND reverse_photo IS NULL
               AND mint_year IS NULL AND numista_url IS NULL AND type_id IS NULL""")
coins_after = con.execute("SELECT COUNT(*) FROM coin").fetchone()[0]

after = con.execute("SELECT COUNT(*) FROM reference").fetchone()[0]
con.commit()
print(f"reference: {before} -> {after}")
print(f"coin: {coins_before} -> {coins_after}")
con.close()
