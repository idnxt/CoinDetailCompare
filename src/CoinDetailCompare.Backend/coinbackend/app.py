"""FastAPI application: local HTTP API for the C# desktop app."""
from __future__ import annotations

import gc
import time
from pathlib import Path

import numpy as np
from fastapi import FastAPI, HTTPException, Request, Response

from . import __version__
from .anomaly import AnomalyDetector
from .config import Config
from .contracts import (
    AnomalyRequest,
    AnomalyResponse,
    CompareRequest,
    CompareResponse,
    EmbedRequest,
    EmbedResponse,
    EmbedSideResult,
    NumistaSearchRequest,
    NumistaSearchResponse,
    PreprocessRequest,
    PreprocessResponse,
    SearchRequest,
    SearchResponse,
    Side,
    VectorPoint,
    VectorProjectRequest,
    VectorProjectResponse,
    VectorRegenRequest,
    VectorRegenResponse,
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


def _cuda_ok() -> bool:
    try:
        import torch
        return torch.cuda.is_available()
    except Exception:
        return False


def _move_models(device: str) -> None:
    """Move every torch model (embedder, anomaly detector) to `device`."""
    import torch

    dev = torch.device(device)
    model = getattr(EMBEDDER, "model", None)
    if model is not None and hasattr(model, "set_device"):
        try:
            model.set_device(dev)
        except Exception:
            pass
    backend = getattr(ANOMALY, "_backend", None)
    if backend:
        name, det = backend
        if name == "anomalydino":
            try:
                det.to(dev)
            except Exception:
                pass


def _ensure_gpu() -> None:
    """Load the models onto the GPU before a GPU request (no-op w/o CUDA)."""
    if CONFIG.device == "gpu" and _cuda_ok():
        try:
            _move_models("cuda")
        except Exception:
            pass


def _release_gpu() -> None:
    """Fully empty the GPU after a request.

    Models are parked on the CPU and the torch caching-allocator pool is
    handed back to the OS, so the backend leaves *no* VRAM behind between
    tasks (single-user app). GPU requests re-arm the models via _ensure_gpu;
    the per-request device transfer over PCIe is far cheaper than holding a
    couple of GB of VRAM all the time."""
    if not _cuda_ok():
        return
    try:
        import torch
        _move_models("cpu")
        gc.collect()
        torch.cuda.empty_cache()
    except Exception:
        pass  # never break a response because of housekeeping


def create_app() -> FastAPI:
    app = FastAPI(title="CoinDetailCompare Backend", version=__version__)

    @app.middleware("http")
    async def _free_gpu_after_request(request: Request, call_next):
        response = await call_next(request)
        _release_gpu()
        return response

    @app.get("/health")
    def health():
        # Report the device the models run on when doing work (CUDA is
        # re-armed per request; between requests everything lives on the CPU).
        device = "cuda" if (CONFIG.device == "gpu" and _cuda_ok()) else "cpu"
        return {
            "status": "ok",
            "version": __version__,
            "device": device,
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
            _ensure_gpu()
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

    @app.get("/debug/vram")
    def debug_vram():
        """What the process actually holds on the GPU right now."""
        try:
            import torch
            out = {"cuda_available": torch.cuda.is_available()}
            if torch.cuda.is_available():
                out["allocated_mb"] = round(torch.cuda.memory_allocated() / 1e6, 1)
                out["reserved_mb"] = round(torch.cuda.memory_reserved() / 1e6, 1)
                stats = torch.cuda.memory_stats()
                out["active_bytes_all"] = stats.get("active_bytes.all")
                out["segment_pool_all"] = stats.get("segment_pool.all")
                model = getattr(EMBEDDER, "model", None)
                out["embedder_device"] = str(model.device) if model is not None else "n/a"
                backend = getattr(ANOMALY, "_backend", None)
                if backend:
                    name, det = backend
                    out["anomaly_backend"] = name
                    if name == "anomalydino":
                        dev = next(det.parameters()).device
                        out["anomaly_detector_device"] = str(dev)
        except Exception as exc:
            out["error"] = str(exc)
        return out

    @app.get("/references")
    def references(kind: str | None = None):
        """Reference lists (quality, rim, orientation, country) for UI dropdowns."""
        return {"items": STORAGE.list_references(kind)}

    @app.post("/embed", response_model=EmbedResponse)
    def embed_endpoint(req: EmbedRequest):
        """Embed one or both crops and persist {id}_{side}.png + .npy + index entry."""
        try:
            import shutil

            _ensure_gpu()
            sides: list[EmbedSideResult] = []
            for side in ("obverse", "reverse"):
                src = req.obverse_path if side == "obverse" else req.reverse_path
                if not src:
                    continue
                img = _read_bgr(src)
                vec = EMBEDDER.embed_image(img)
                photo = STORAGE.photo_path(req.coin_id, side)
                shutil.copyfile(src, photo)
                _write_bgr(photo, img)  # re-encode consistent (BGR, no EXIF)
                vpath = STORAGE.vector_path(req.coin_id, side)
                np.save(str(vpath), vec.astype("float32"))
                SEARCH.add_vector(req.coin_id, side, vec)
                sides.append(
                    EmbedSideResult(
                        side=side,
                        photo_path=str(photo),
                        vector_path=str(vpath),
                        dim=int(vec.shape[0]),
                    )
                )
            SEARCH.persist()
            return EmbedResponse(ok=True, coin_id=req.coin_id, sides=sides)
        except Exception as exc:
            return EmbedResponse(ok=False, coin_id=req.coin_id, error=str(exc))

    @app.post("/anomaly", response_model=AnomalyResponse)
    def anomaly_endpoint(req: AnomalyRequest):
        try:
            if (req.device or "").lower() != "cpu":
                _ensure_gpu()
            result = ANOMALY.detect(
                crop_path=req.crop_path,
                side=req.side,
                reference_ids=req.reference_ids,
                heatmap_size=req.heatmap_size,
                threshold=req.threshold,
                coin_id=req.coin_id,
                device=req.device,
                patch_grid=req.patch_grid,
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

    @app.post("/compare", response_model=CompareResponse)
    def compare_endpoint(req: CompareRequest):
        """Rotation-aligned diff layer (query vs etalon) + 2-D vector projection."""
        from .diffmap import aligned_pair, back_to_query, diff_image, project_2d

        try:
            _ensure_gpu()
            query = _read_bgr(req.crop_path)
            etalon_path = STORAGE.photo_path(req.etalon_coin_id, req.side)
            etalon = _read_bgr(str(etalon_path))

            qn, rc, deg, m_q = aligned_pair(query, etalon)
            norm_path, diff_path = None, None
            mean = 0.0
            if req.coin_id is not None:
                norm_path = STORAGE.normalized_path(req.coin_id, req.side)
                diff_path = STORAGE.cfg.coimg_dir / f"{req.coin_id}_{req.side}_diff.png"
                _write_bgr(norm_path, qn)
                out, mean = diff_image(qn, rc)
                # Put the layer back in the raw query frame so the UI overlay
                # covers the whole displayed coin (see back_to_query).
                hq, wq = query.shape[:2]
                _write_bgr(diff_path, back_to_query(out, m_q, (wq, hq)))

            # 2-D projection of the search vectors: query + etalon candidates.
            ids = list(dict.fromkeys([req.etalon_coin_id, *req.candidate_ids]))
            vectors: list[np.ndarray] = [EMBEDDER.embed_image(query)]
            labels: list[str] = ["query"]
            is_query: list[bool] = [True]
            for cid in ids:
                vpath = STORAGE.vector_path(cid, req.side)
                vec = (
                    np.load(str(vpath))
                    if vpath.exists()
                    else EMBEDDER.embed_image(_read_bgr(str(STORAGE.photo_path(cid, req.side))))
                )
                vectors.append(vec)
                labels.append(f"#{cid}")
                is_query.append(False)
            pts = project_2d(vectors)
            points = [
                VectorPoint(x=round(px, 4), y=round(py, 4), label=lab, is_query=q)
                for (px, py), lab, q in zip(pts, labels, is_query)
            ]
            return CompareResponse(
                ok=True,
                normalized_path=str(norm_path) if norm_path else None,
                diff_path=str(diff_path) if diff_path else None,
                mean_diff=round(mean, 4),
                rotation_deg=round(deg, 2),
                points=points,
            )
        except Exception as exc:
            return CompareResponse(ok=False, mean_diff=0.0, error=str(exc))

    @app.post("/index/rebuild")
    def rebuild_index():
        return SEARCH.rebuild_from_disk()

    @app.post("/vector/project", response_model=VectorProjectResponse)
    def vector_project_endpoint(req: VectorProjectRequest):
        """2-D projection of one coin's own search vector, shown against the
        other coins (explicit ids, or all coins that have a vector for the side)."""
        from .diffmap import project_2d

        try:
            _ensure_gpu()
            vectors = [_load_vector(req.coin_id, req.side)]
            labels = [f"#{req.coin_id}"]
            is_query = [True]
            ids = [i for i in dict.fromkeys(req.other_ids) if i != req.coin_id]
            if not ids:
                # no explicit set: every other coin with a vector for this side
                ids = [cid for cid, _ in STORAGE.iter_vectors(req.side) if cid != req.coin_id][:49]
            for cid in ids:
                try:
                    vectors.append(_load_vector(cid, req.side))
                    labels.append(f"#{cid}")
                    is_query.append(False)
                except Exception:
                    continue  # coin without photo/vector is simply skipped
            pts = project_2d(vectors)
            return VectorProjectResponse(ok=True, points=[
                VectorPoint(x=round(px, 4), y=round(py, 4), label=lab, is_query=q)
                for (px, py), lab, q in zip(pts, labels, is_query)
            ])
        except Exception as exc:
            return VectorProjectResponse(ok=False, error=str(exc))

    @app.get("/compare/image")
    def compare_image_endpoint(coin_id: int, etalon_coin_id: int, side: str):
        """Transparent diff layer between the coin and its etalon (one side):
        direct |a - b| of the two raw crop photos, no rotation alignment
        (the card aligns the etalon photo manually).  White pixels whose
        alpha follows the diff intensity (0 outside the coin disc); the card
        tints the grayscale draft client-side (color/opacity settings)."""
        import cv2  # type: ignore

        try:
            qpath = STORAGE.photo_path(coin_id, side)
            epath = STORAGE.photo_path(etalon_coin_id, side)
            if not qpath.exists() or not epath.exists():
                raise FileNotFoundError(f"missing photos: {qpath} / {epath}")
            query = _read_bgr(str(qpath))
            etalon = _read_bgr(str(epath))
            hq, wq = query.shape[:2]
            if etalon.shape[:2] != (hq, wq):
                etalon = cv2.resize(etalon, (wq, hq), interpolation=cv2.INTER_LINEAR)
            ga = cv2.cvtColor(query, cv2.COLOR_BGR2GRAY).astype("float32")
            gb = cv2.cvtColor(etalon, cv2.COLOR_BGR2GRAY).astype("float32")
            d = np.abs(ga - gb)
            # The tight square crop: the coin is the inscribed circle.
            r = int(0.498 * min(hq, wq))
            y, x = np.ogrid[:hq, :wq]
            inside = (x - wq / 2) ** 2 + (y - hq / 2) ** 2 <= r * r
            peak = float(d[inside].max()) if inside.any() else 0.0
            if peak > 1e-6:
                d = d / peak * 255.0
            d = np.where(inside, np.clip(d, 0, 255), 0).astype("uint8")
        except Exception as exc:
            raise HTTPException(status_code=404, detail=str(exc))
        rgba = np.zeros((hq, wq, 4), dtype="uint8")
        rgba[..., :3] = 255
        rgba[..., 3] = d
        ok, buf = cv2.imencode(".png", rgba)
        if not ok:
            raise HTTPException(status_code=500, detail="could not encode PNG")
        return Response(content=buf.tobytes(), media_type="image/png")

    @app.post("/vector/regenerate", response_model=VectorRegenResponse)
    def vector_regenerate_endpoint(req: VectorRegenRequest):
        """Re-embed the stored photo(s) of a coin, replace its .npy vector and
        refresh the FAISS entry (an etalon has no heatmap to rebuild, but it
        always has a vector)."""
        _ensure_gpu()
        sides = [req.side] if req.side else ("obverse", "reverse")
        done: list[str] = []
        dim = None
        for side in sides:
            photo = STORAGE.photo_path(req.coin_id, side)
            if not photo.exists():
                continue
            img = _read_bgr(str(photo))
            vec = EMBEDDER.embed_image(img)
            vpath = STORAGE.vector_path(req.coin_id, side)
            np.save(str(vpath), vec.astype("float32"))
            SEARCH.add_vector(req.coin_id, side, vec)
            done.append(side)
            dim = int(vec.shape[0])
        SEARCH.persist()
        if not done:
            return VectorRegenResponse(ok=False, coin_id=req.coin_id, error="no stored photos")
        return VectorRegenResponse(ok=True, coin_id=req.coin_id, sides=done, dim=dim)

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
            if payload.get("fetch_meta"):
                return NUMISTA.fetch_meta(link)
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


def _load_vector(coin_id: int, side: str) -> "np.ndarray":
    """Saved .npy vector of a coin, or a fresh embedding from its stored photo."""
    vpath = STORAGE.vector_path(coin_id, side)
    if vpath.exists():
        return np.load(str(vpath))
    return EMBEDDER.embed_image(_read_bgr(str(STORAGE.photo_path(coin_id, side))))


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
