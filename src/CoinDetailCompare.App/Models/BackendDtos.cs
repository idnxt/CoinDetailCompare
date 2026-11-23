using System.Text.Json.Serialization;

namespace CoinDetailCompare.App.Models;

// DTOs mirror the JSON contracts in shared/contracts/*.json

public record HealthDto(
    string status,
    string version,
    string device,
    string model,
    int embedding_dim,
    string embedder,
    string coimg_dir);

public record PreprocessRequest(
    string raw_path,
    string side,
    int target_size,
    double orientation_deg,
    string? etalon_path);

public record PreprocessResponse(
    bool ok,
    string? crop_path,
    int? size,
    string? shape_detected,
    double orientation_applied_deg,
    string? vector_path,
    int? embedding_dim,
    string? error);

public record SearchRequest(
    string crop_path,
    string side,
    string? obverse_path,
    string? reverse_path,
    int? candidate_type_id,
    string? etalon_quality,
    int topk);

public record EtalonPhoto(string? obverse, string? reverse);

public record CandidateDto(
    int coin_id,
    int? type_id,
    string? quality,
    double obverse_pct,
    double reverse_pct,
    double overall_pct,
    string orientation,
    EtalonPhoto etalon_photo);

public record SideDto(int coin_id, double pct);

public record SearchResponse(
    string query_side,
    List<CandidateDto> candidates,
    Dictionary<string, SideDto> sides);

public record EmbedRequest(
    int coin_id,
    string? obverse_path,
    string? reverse_path);

public record EmbedSideResult(
    string side,
    string? photo_path,
    string? vector_path,
    int? dim);

public record EmbedResponse(
    bool ok,
    int coin_id,
    List<EmbedSideResult> sides,
    string? error);

public record AnomalyRequest(
    string crop_path,
    string side,
    List<int> reference_ids,
    int heatmap_size,
    double? threshold,
    int? coin_id,
    string? device = null,
    int? patch_grid = null);

public record BoundingBoxDto(double x, double y, double w, double h);

public record TopPatchDto(int row, int col, double distance);

public record AnomalyResponse(
    bool is_anomaly,
    double score,
    string? heatmap_path,
    string? heatmap_matrix_path,
    int heatmap_size,
    List<BoundingBoxDto> bounding_boxes,
    List<TopPatchDto> top_patches,
    string? error);

public record ReferenceItemDto(string kind, string? code, string? ru, string? en);

public record ReferencesResponse(List<ReferenceItemDto> items);

public record NumistaParseResponse(
    int? numista_id,
    string? kind,
    string? url,
    string? title,
    string? source);

public record NumistaSearchRequest(string query, int limit);

public record NumistaItemDto(
    int? numista_id,
    string? title,
    string? url,
    string? country,
    string? denomination,
    string? year);

public record NumistaSearchResponse(string query, List<NumistaItemDto> items, string? error);
