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


class AnomalyRequest(BaseModel):
    crop_path: str
    side: str = "obverse"
    reference_ids: list[int] = Field(default_factory=list)
    heatmap_size: int = 128
    threshold: float | None = None


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
    heatmap_size: int
    bounding_boxes: list[BoundingBox] = Field(default_factory=list)
    top_patches: list[TopPatch] = Field(default_factory=list)
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
