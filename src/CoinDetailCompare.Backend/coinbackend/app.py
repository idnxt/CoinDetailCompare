"""FastAPI application: local HTTP API for the C# desktop app."""
from __future__ import annotations

import time
from pathlib import Path

from fastapi import FastAPI, HTTPException

from . import __version__
from .anomaly import AnomalyDetector
from .config import Config
from .contracts import (
    AnomalyRequest,
    AnomalyResponse,
    NumistaSearchRequest,
    NumistaSearchResponse,
    PreprocessRequest,
    PreprocessResponse,
    SearchRequest,
    SearchResponse,
    Side,
)
from .embed import get_embedder
from .numista import NumistaClient, parse_manual_numista_link
from .preprocess import preprocess
from .search import SearchEngine
from .storage import Storage

# --- singletons (built once at import; lazy heavy deps inside) ---
CONFIG = Config.load()
STORAGE = Storage(CONFIG)
EMBEDDER = get_embedder(CONFIG)
SEARCH = SearchEngine(CONFIG, STORAGE)
ANOMALY = AnomalyDetector(CONFIG, STORAGE, EMBEDDER)
NUMISTA = NumistaClient(CONFIG)


def create_app() -> FastAPI:
    app = FastAPI(title="CoinDetailCompare Backend", version=__version__)

    @app.get("/health")
    def health():
        return {
            "status": "ok",
            "version": __version__,
            "device": CONFIG.device,
            "model": CONFIG.model,
            "embedding_dim": EMBEDDER.dim,
            "embedder": type(EMBEDDER).__name__,
            "coimg_dir": str(CONFIG.coimg_dir),
        }

    @app.post("/preprocess", response_model=PreprocessResponse)
    def preprocess_endpoint(req: PreprocessRequest):
        try:
            res = preprocess(
                raw_path=req.raw_path,
                side=req.side,
                target_size=req.target_size,
                min_size=CONFIG.min_size,
                background_color=CONFIG.background_color,
                orientation_deg=req.orientation_deg,
                etalon_path=req.etalon_path,
            )
            img = res.pop("image")
            out_path = STORAGE.tmp_path(f"crop_{req.side}_{int(time.time())}.png")
            _write_bgr(out_path, img)
            return PreprocessResponse(
                ok=True,
                crop_path=str(out_path),
                size=res["size"],
                shape_detected=res["shape_detected"],
                orientation_applied_deg=res["orientation_applied_deg"],
            )
        except Exception as exc:
            return PreprocessResponse(ok=False, error=str(exc))

    @app.post("/search", response_model=SearchResponse)
    def search_endpoint(req: SearchRequest):
        try:
            topk = req.topk or CONFIG.topk
            ob_path = req.obverse_path or (req.crop_path if req.side == "obverse" else None)
            rev_path = req.reverse_path or (req.crop_path if req.side == "reverse" else None)

            if ob_path and rev_path:
                # Both sides available -> check both permutations (A-A+R-R vs A-R+R-A).
                ov_vec = EMBEDDER.embed_image(_read_bgr(ob_path))
                rv_vec = EMBEDDER.embed_image(_read_bgr(rev_path))
                both = SEARCH.search_both_sides(ov_vec, rv_vec, topk)
                candidates = [_enrich(c) for c in both]
                query_side = "both"
                sides: dict[str, Side] = {}
                if candidates:
                    sides["obverse"] = Side(coin_id=candidates[0]["coin_id"], pct=candidates[0]["obverse_pct"])
                    sides["reverse"] = Side(coin_id=candidates[0]["coin_id"], pct=candidates[0]["reverse_pct"])
            else:
                # Single side only -> rank that side.
                vec = EMBEDDER.embed_image(_read_bgr(req.crop_path))
                ranked = SEARCH.search_side(req.side, vec, topk)
                candidates = [_candidate_from_db(cid, s, req.side) for cid, s in ranked]
                query_side = req.side
                sides = (
                    {req.side: Side(coin_id=candidates[0]["coin_id"], pct=candidates[0]["overall_pct"])}
                    if candidates
                    else {}
                )

            return SearchResponse(query_side=query_side, candidates=candidates, sides=sides)
        except Exception as exc:
            raise HTTPException(status_code=400, detail=str(exc))

    @app.post("/anomaly", response_model=AnomalyResponse)
    def anomaly_endpoint(req: AnomalyRequest):
        try:
            result = ANOMALY.detect(
                crop_path=req.crop_path,
                side=req.side,
                reference_ids=req.reference_ids,
                heatmap_size=req.heatmap_size,
                threshold=req.threshold,
            )
            return AnomalyResponse(**result)
        except NotImplementedError as exc:
            return AnomalyResponse(
                is_anomaly=False, score=0.0, heatmap_size=req.heatmap_size, error=str(exc)
            )
        except Exception as exc:
            return AnomalyResponse(
                is_anomaly=False, score=0.0, heatmap_size=req.heatmap_size, error=str(exc)
            )

    @app.post("/index/rebuild")
    def rebuild_index():
        return SEARCH.rebuild_from_disk()

    @app.post("/numista/search", response_model=NumistaSearchResponse)
    def numista_search_endpoint(req: NumistaSearchRequest):
        items = NUMISTA.search(req.query, req.limit)
        if items and items[0].get("error"):
            return NumistaSearchResponse(query=req.query, items=[], error=items[0]["error"])
        return NumistaSearchResponse(query=req.query, items=items)

    @app.post("/numista/parse")
    def numista_parse_endpoint(payload: dict):
        link = payload.get("link", "")
        try:
            return parse_manual_numista_link(link)
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc))

    return app


def _candidate_from_db(coin_id: int, score: float, side: str) -> dict:
    pct = max(0.0, min(100.0, float(score) * 100.0))
    row = _coin_row(coin_id)
    return {
        "coin_id": coin_id,
        "type_id": row.get("type_id") if row else None,
        "quality": row.get("quality") if row else None,
        "obverse_pct": pct if side == "obverse" else 0.0,
        "reverse_pct": pct if side == "reverse" else 0.0,
        "overall_pct": pct,
        "orientation": "A-A+R-R",
        "etalon_photo": {
            "obverse": row.get("obverse_photo") if row else None,
            "reverse": row.get("reverse_photo") if row else None,
        },
    }


def _enrich(base: dict) -> dict:
    """Attach DB metadata (type_id, quality, etalon photos) to a both-sides result."""
    row = _coin_row(base["coin_id"])
    out = dict(base)
    if row:
        out["type_id"] = row.get("type_id")
        out["quality"] = row.get("quality")
        out["etalon_photo"] = {
            "obverse": row.get("obverse_photo"),
            "reverse": row.get("reverse_photo"),
        }
    else:
        out["type_id"] = None
        out["quality"] = None
        out["etalon_photo"] = {"obverse": None, "reverse": None}
    return out


def _coin_row(coin_id: int):
    with STORAGE.connect() as con:
        cur = con.execute("SELECT * FROM coin WHERE id = ?", (coin_id,))
        row = cur.fetchone()
        return dict(row) if row else None


def _read_bgr(path: str):
    import cv2  # type: ignore

    img = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if img is None:
        raise HTTPException(status_code=400, detail=f"Could not read image: {path}")
    return img


def _write_bgr(path: Path, img) -> None:
    import cv2  # type: ignore

    path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(path), img)


app = create_app()


def main() -> None:
    import uvicorn  # type: ignore

    uvicorn.run("coinbackend.app:app", host=CONFIG.host, port=CONFIG.port, reload=False)


if __name__ == "__main__":
    main()
