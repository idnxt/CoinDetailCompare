"""JSON contracts for the C# <-> Python API.

Mirrors the example payloads in shared/contracts/*.json.
"""
from __future__ import annotations

from pydantic import BaseModel, Field


class Side(BaseModel):
    coin_id: int
    pct: float


class EtalonPhoto(BaseModel):
    obverse: str | None = None
    reverse: str | None = None


class Candidate(BaseModel):
    coin_id: int
    type_id: int | None = None
    quality: str | None = None
    obverse_pct: float = 0.0
    reverse_pct: float = 0.0
    overall_pct: float = 0.0
    orientation: str = "A-A+R-R"  # A-A+R-R | A-R+R-A
    etalon_photo: EtalonPhoto = Field(default_factory=EtalonPhoto)


class SearchRequest(BaseModel):
    # Primary crop (the side being searched when only one side is available).
    crop_path: str
    side: str = "obverse"  # obverse | reverse
    # Optional: both sides, to enable the permutation check (A-A+R-R vs A-R+R-A).
    obverse_path: str | None = None
    reverse_path: str | None = None
    candidate_type_id: int | None = None
    etalon_quality: str | None = None
    topk: int = 10


class SearchResponse(BaseModel):
    query_side: str
    oriented: bool = True
    candidates: list[Candidate]
    sides: dict[str, Side] = Field(default_factory=dict)


class PreprocessRequest(BaseModel):
    raw_path: str
    side: str = "obverse"
    target_size: int = 1024
    orientation_deg: float = 0.0  # manual angle; 0.0 -> auto align
    # Optional etalon crop for auto-alignment (e.g. the matched reference coin).
    etalon_path: str | None = None


class PreprocessResponse(BaseModel):
    ok: bool
    crop_path: str | None = None
    size: int | None = None
    shape_detected: str | None = None
    orientation_applied_deg: float = 0.0
    vector_path: str | None = None
    embedding_dim: int | None = None
    error: str | None = None


class EmbedRequest(BaseModel):
    coin_id: int
    obverse_path: str | None = None
    reverse_path: str | None = None


class EmbedSideResult(BaseModel):
    side: str
    photo_path: str | None = None
    vector_path: str | None = None
    dim: int | None = None


class EmbedResponse(BaseModel):
    ok: bool
    coin_id: int
    sides: list[EmbedSideResult] = Field(default_factory=list)
    error: str | None = None


class AnomalyRequest(BaseModel):
    crop_path: str
    side: str = "obverse"
    reference_ids: list[int] = Field(default_factory=list)
    heatmap_size: int = 128
    threshold: float | None = None
    # When known, heatmap files are persisted under this coin id.
    coin_id: int | None = None
    # Optional runtime device override: "cpu" | "gpu" (falls back to CPU
    # automatically when the GPU is unavailable / has no kernels).
    device: str | None = None
    # Patch grid side (min..max, e.g. 8..128) for the patch-level detector.
    patch_grid: int | None = None


class BoundingBox(BaseModel):
    x: float
    y: float
    w: float
    h: float


class TopPatch(BaseModel):
    row: int
    col: int
    distance: float


class AnomalyResponse(BaseModel):
    is_anomaly: bool
    score: float
    heatmap_path: str | None = None
    heatmap_matrix_path: str | None = None
    # Rotation-normalized query image (canonical frame), if persisted.
    normalized_path: str | None = None
    heatmap_size: int
    bounding_boxes: list[BoundingBox] = Field(default_factory=list)
    top_patches: list[TopPatch] = Field(default_factory=list)
    error: str | None = None


class CompareRequest(BaseModel):
    """Comparison layer: query coin vs a chosen etalon candidate."""
    crop_path: str
    side: str = "obverse"
    etalon_coin_id: int
    # When known, normalized/diff files are persisted under this coin id.
    coin_id: int | None = None
    # Extra etalon coin ids (the whole candidate list) for the 2-D vector
    # projection; the primary etalon is always included.
    candidate_ids: list[int] = Field(default_factory=list)


class VectorPoint(BaseModel):
    x: float
    y: float
    label: str
    is_query: bool = False


class CompareResponse(BaseModel):
    ok: bool
    normalized_path: str | None = None
    diff_path: str | None = None
    mean_diff: float = 0.0
    rotation_deg: float = 0.0
    points: list[VectorPoint] = Field(default_factory=list)
    error: str | None = None


class VectorProjectRequest(BaseModel):
    """2-D projection of a single coin's search vector (plus other coins,
    explicitly listed or all coins that have a vector for that side)."""
    coin_id: int
    side: str = "obverse"
    other_ids: list[int] = Field(default_factory=list)


class VectorProjectResponse(BaseModel):
    ok: bool
    points: list[VectorPoint] = Field(default_factory=list)
    error: str | None = None


class VectorRegenRequest(BaseModel):
    coin_id: int
    # None -> every side that has a stored photo
    side: str | None = None


class VectorRegenResponse(BaseModel):
    ok: bool
    coin_id: int
    sides: list[str] = Field(default_factory=list)
    dim: int | None = None
    error: str | None = None


class NumistaSearchRequest(BaseModel):
    query: str
    limit: int = 20


class NumistaItem(BaseModel):
    numista_id: int | None = None
    title: str | None = None
    url: str | None = None
    country: str | None = None
    denomination: str | None = None
    year: str | None = None


class NumistaSearchResponse(BaseModel):
    query: str
    items: list[NumistaItem]
    error: str | None = None
