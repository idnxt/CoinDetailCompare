using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoinDetailCompare.App.Models;
using CoinDetailCompare.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoinDetailCompare.App.ViewModels;

/// <summary>
/// Card of a saved coin: the same layers as during photo — base photo,
/// anomaly heatmap, rotation-normalized coin, diff against the etalon,
/// 2-D vector projection and the annotation layer. Attributes (name,
/// album/page/number, year, mint, grade, anomaly description) are editable
/// and saved back to the catalog database.
/// </summary>
public class CoinCardViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly BackendClient _backend;
    private readonly DbService _db;
    private readonly LocalizationService _loc;

    public int CoinId { get; }
    private int? _etalonId;
    public int? EtalonId => _etalonId;

    /// <summary>Base coin (no etalon above it): nothing to compare against —
    /// no heatmap/anomaly/diff, but it has its own search vector.</summary>
    private bool _isBase;
    public bool IsBase
    {
        get => _isBase;
        private set
        {
            if (!SetProperty(ref _isBase, value)) return;
            OnPropertyChanged(nameof(Blocked));
        }
    }

    // ---- active side ----
    private CoinSide _side = CoinSide.Obverse;
    public CoinSide Side
    {
        get => _side;
        private set
        {
            if (!SetProperty(ref _side, value)) return;
            OnPropertyChanged(nameof(SideName));
            RefreshImages();
        }
    }

    public string SideName =>
        Side == CoinSide.Obverse ? T("capture.obverse") : T("capture.reverse");

    // ---- layers ----
    private ImageSource? _baseImage;
    public ImageSource? BaseImage
    {
        get => _baseImage;
        private set => SetProperty(ref _baseImage, value);
    }

    private ImageSource? _heatmapImage;
    public ImageSource? HeatmapImage
    {
        get => _heatmapImage;
        private set => SetProperty(ref _heatmapImage, value);
    }

    private ImageSource? _diffImage;
    public ImageSource? DiffImage
    {
        get => _diffImage;
        private set => SetProperty(ref _diffImage, value);
    }

    private List<VectorPointItem> _vectorPoints = new();
    public List<VectorPointItem> VectorPoints
    {
        get => _vectorPoints;
        private set
        {
            SetProperty(ref _vectorPoints, value);
            HasVectorPoints = value.Count > 0;
        }
    }

    private bool _hasVectorPoints;
    public bool HasVectorPoints
    {
        get => _hasVectorPoints;
        private set => SetProperty(ref _hasVectorPoints, value);
    }

    private bool _showHeatmap = true;
    public bool ShowHeatmap
    {
        get => _showHeatmap;
        set => SetProperty(ref _showHeatmap, value);
    }

    private bool _showDiff = true;
    public bool ShowDiff
    {
        get => _showDiff;
        set => SetProperty(ref _showDiff, value);
    }

    private bool _showAnnotations = true;
    public bool ShowAnnotations
    {
        get => _showAnnotations;
        set => SetProperty(ref _showAnnotations, value);
    }

    // ---- anomaly result ----
    private double _anomalyScore;
    public double AnomalyScore
    {
        get => _anomalyScore;
        private set => SetProperty(ref _anomalyScore, value);
    }

    private bool _isAnomaly;
    public bool IsAnomaly
    {
        get => _isAnomaly;
        set => SetProperty(ref _isAnomaly, value);
    }

    // ---- coin kind: etalon (Э) or duplicate (Д). Anomaly is a separate
    // flag — an etalon coin can also be marked as an anomaly. ----
    private bool _isDuplicate;
    public bool IsDuplicate
    {
        get => _isDuplicate;
        set => SetProperty(ref _isDuplicate, value);
    }

    private string _anomalyStatus = "";
    public string AnomalyStatus
    {
        get => _anomalyStatus;
        private set => SetProperty(ref _anomalyStatus, value);
    }

    // ---- editable attributes ----
    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _album = "";
    public string Album
    {
        get => _album;
        set => SetProperty(ref _album, value);
    }

    private int _pageNo;
    public int PageNo
    {
        get => _pageNo;
        set => SetProperty(ref _pageNo, value);
    }

    private int _orderNo;
    public int OrderNo
    {
        get => _orderNo;
        set => SetProperty(ref _orderNo, value);
    }

    private string _mintYear = "";
    public string MintYear
    {
        get => _mintYear;
        set => SetProperty(ref _mintYear, value);
    }

    private string _mint = "";
    public string Mint
    {
        get => _mint;
        set => SetProperty(ref _mint, value);
    }

    private string _quality = "";
    public string Quality
    {
        get => _quality;
        set => SetProperty(ref _quality, value);
    }

    private string _anomalyDesc = "";
    public string AnomalyDesc
    {
        get => _anomalyDesc;
        set => SetProperty(ref _anomalyDesc, value);
    }

    private string _etalonLabel = "";
    public string EtalonLabel
    {
        get => _etalonLabel;
        private set => SetProperty(ref _etalonLabel, value);
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(Blocked));
        }
    }

    /// <summary>Busy or a base coin: comparison/anomaly buttons stay disabled.</summary>
    public bool Blocked => IsBusy || IsBase;

    // ---- annotation layer ----
    public ObservableCollection<AnnotationItem> AnnotationItems { get; } = new();

    private AnnotationTool _annotationTool = AnnotationTool.Path;
    public AnnotationTool AnnotationTool
    {
        get => _annotationTool;
        set => SetProperty(ref _annotationTool, value);
    }

    private string _annotationToolName = "";
    public string AnnotationToolName
    {
        get => _annotationToolName;
        set
        {
            if (!SetProperty(ref _annotationToolName, value)) return;
            // order in AnnotationToolItems: line, ellipse, arrow
            AnnotationTool = AnnotationToolItems.IndexOf(value) switch
            {
                1 => AnnotationTool.Ellipse,
                2 => AnnotationTool.Arrow,
                _ => AnnotationTool.Path,
            };
        }
    }

    public List<string> AnnotationToolItems { get; } = new();

    public CoinCardViewModel(int coinId, AppSettings settings, BackendClient backend,
        DbService db, LocalizationService loc)
    {
        CoinId = coinId;
        _settings = settings;
        _backend = backend;
        _db = db;
        _loc = loc;

        AnnotationToolItems.Add(_loc.T("annotation.tool_line"));
        AnnotationToolItems.Add(_loc.T("annotation.tool_ellipse"));
        AnnotationToolItems.Add(_loc.T("annotation.tool_arrow"));
        _annotationToolName = _loc.T("annotation.tool_line");

        SwapSideCommand = new RelayCommand(SwapSide);
        RunAnomalyCommand = new RelayCommand(async () => await RunAnomalyAsync());
        RunCompareCommand = new RelayCommand(async () => await RunCompareAsync());
        RegenerateVectorCommand = new RelayCommand(async () => await RegenerateVectorAsync());
        SaveCommand = new RelayCommand(Save);
        UndoAnnotationCommand = new RelayCommand(UndoAnnotation);
        ClearAnnotationsCommand = new RelayCommand(ClearAnnotations);
        SaveAnnotationsCommand = new RelayCommand(SaveAnnotations);

        _ = InitAsync();
    }

    public RelayCommand SwapSideCommand { get; }
    public RelayCommand RunAnomalyCommand { get; }
    public RelayCommand RunCompareCommand { get; }
    public RelayCommand RegenerateVectorCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand UndoAnnotationCommand { get; }
    public RelayCommand ClearAnnotationsCommand { get; }
    public RelayCommand SaveAnnotationsCommand { get; }

    public string T(string key) => _loc.T(key);

    /// <summary>Card header line: "Карточка монеты №{id}".</summary>
    public string Header => string.Format(_loc.T("card.title"), CoinId);

    /// <summary>Load the coin from the DB and run the comparison layers.</summary>
    public async Task InitAsync()
    {
        var c = _db.GetCoin(CoinId);
        if (c is null)
        {
            Status = T("status.error");
            return;
        }
        _etalonId = c.EtalonId;
        EtalonLabel = _etalonId is int eid
            ? string.Format(T("card.etalon"), eid)
            : T("card.no_etalon");

        Name = c.TypeId is int tid ? _db.GetCoinType(tid)?.Name ?? "" : "";
        Album = c.AlbumName ?? "";
        PageNo = c.PageNo ?? 0;
        OrderNo = c.OrderNo ?? 0;
        MintYear = c.MintYear ?? "";
        Mint = c.Mint ?? "";
        Quality = c.Quality ?? "";
        AnomalyDesc = c.AnomalyDesc ?? "";
        IsAnomaly = c.IsAnomaly;
        IsDuplicate = c.SourceKind is SourceKind.Duplicate or SourceKind.Variant or SourceKind.Anomaly;
        IsBase = _etalonId is null;

        RefreshImages();

        // A base coin has no etalon to compare against (no diff/anomaly), but
        // its own search vector is projected against the other coins.
        if (IsBase)
            _ = LoadVectorAsync();
        else
            _ = RunCompareAsync();
        try
        {
            var h = await _backend.HealthAsync();
            if (h is { device: "cuda" } && !IsBase)
                _ = RunAnomalyAsync();
        }
        catch
        {
            // backend offline: layers stay as the saved files on disk
        }
    }

    private void SwapSide() => Side = Side == CoinSide.Obverse ? CoinSide.Reverse : CoinSide.Obverse;

    private string SideNameRaw() => Side == CoinSide.Obverse ? "obverse" : "reverse";

    private string? PhotoPath(Coin? c)
    {
        var p = Side == CoinSide.Obverse ? c?.ObversePhoto : c?.ReversePhoto;
        return string.IsNullOrEmpty(p) || !File.Exists(p!) ? null : p;
    }

    /// <summary>Reload the layer images for the active side from the saved files.</summary>
    private void RefreshImages()
    {
        var c = _db.GetCoin(CoinId);
        var photo = PhotoPath(c);
        BaseImage = photo is not null ? LoadImage(photo) : null;
        string side = SideNameRaw();
        // A base coin has no etalon -> its heatmap/diff layers are meaningless.
        HeatmapImage = IsBase ? null : LoadIfExists(Path.Combine(_settings.CoimgDir, $"{CoinId}_{side}_heatmap.png"));
        DiffImage = LoadIfExists(Path.Combine(_settings.CoimgDir, $"{CoinId}_{side}_diff.png"));
        VectorPoints = new List<VectorPointItem>();
        AnomalyStatus = "";
        LoadAnnotationLayer();
        if (IsBase)
            _ = LoadVectorAsync();
    }

    /// <summary>Compare the active side with the etalon: rotation-aligned diff
    /// layer, normalized coin and the 2-D vector projection.</summary>
    private async Task RunCompareAsync()
    {
        var c = _db.GetCoin(CoinId);
        var photo = PhotoPath(c);
        if (photo is null || _etalonId is not int eid) return;
        IsBusy = true;
        try
        {
            var resp = await _backend.CompareAsync(new CompareRequest(
                crop_path: photo,
                side: SideNameRaw(),
                etalon_coin_id: eid,
                coin_id: CoinId,
                candidate_ids: new List<int> { eid }));
            if (resp is not { ok: true })
            {
                Status = $"{T("status.error")}: {resp?.error ?? "no response"}";
                return;
            }
            // The diff layer is warped back to the raw crop frame (see
            // back_to_query), so keep the raw crop as the base image here —
            // swapping to the normalized coin would shift the overlay.
            DiffImage = resp.diff_path is not null && File.Exists(resp.diff_path)
                ? LoadImage(resp.diff_path)
                : null;
            VectorPoints = resp.points.Select(p => new VectorPointItem
            {
                X = p.x,
                Y = p.y,
                Label = p.label,
                IsQuery = p.is_query,
            }).ToList();
            Status = string.Format(T("card.compare_done"), resp.mean_diff, resp.rotation_deg);
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>2-D projection of this coin's own search vector against the
    /// other coins (for a base coin there is no etalon to compare with).</summary>
    private async Task LoadVectorAsync()
    {
        try
        {
            var resp = await _backend.ProjectVectorAsync(new VectorProjectRequest(CoinId, SideNameRaw(), new List<int>()));
            if (resp is { ok: true })
            {
                VectorPoints = resp.points.Select(p => new VectorPointItem
                {
                    X = p.x,
                    Y = p.y,
                    Label = p.label,
                    IsQuery = p.is_query,
                }).ToList();
            }
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
        }
    }

    /// <summary>Re-embed the stored photo of the active side: new .npy vector
    /// plus a refreshed index entry, then update the projection.</summary>
    private async Task RegenerateVectorAsync()
    {
        IsBusy = true;
        try
        {
            var resp = await _backend.RegenerateVectorAsync(new VectorRegenRequest(CoinId, SideNameRaw()));
            if (resp is not { ok: true })
            {
                Status = $"{T("status.error")}: {resp?.error ?? "no response"}";
                return;
            }
            Status = string.Format(T("card.vector_recreated"), resp.dim);
            _ = LoadVectorAsync();
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Run the anomaly detector on the active side (etalon as reference).</summary>
    private async Task RunAnomalyAsync()
    {
        var c = _db.GetCoin(CoinId);
        var photo = PhotoPath(c);
        if (photo is null || IsBase) return;
        IsBusy = true;
        try
        {
            var refs = new List<int>();
            if (_etalonId is int eid) refs.Add(eid);
            var resp = await _backend.AnomalyAsync(new AnomalyRequest(
                crop_path: photo,
                side: SideNameRaw(),
                reference_ids: refs,
                heatmap_size: 128,
                threshold: null,
                coin_id: CoinId,
                device: "gpu",
                patch_grid: 16));
            if (resp is null)
            {
                AnomalyStatus = T("status.error");
                return;
            }
            AnomalyScore = resp.score;
            IsAnomaly = resp.is_anomaly;
            AnomalyStatus = resp.is_anomaly
                ? $"{T("anomaly.detected")} ({resp.score:F2})"
                : $"{T("anomaly.ok")} ({resp.score:F2})";
            if (resp.heatmap_path is not null && File.Exists(resp.heatmap_path))
                HeatmapImage = LoadImage(resp.heatmap_path);
            if (resp.normalized_path is not null && File.Exists(resp.normalized_path))
                BaseImage = LoadImage(resp.normalized_path);
        }
        catch (System.Exception ex)
        {
            AnomalyStatus = $"{T("status.error")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Persist attributes; the name goes to the coin_type record
    /// (created on first save), which is what the etalon-candidate list shows.</summary>
    private void Save()
    {
        try
        {
            var c = _db.GetCoin(CoinId) ?? new Coin { Id = CoinId };
            c.AlbumName = string.IsNullOrWhiteSpace(Album) ? null : Album;
            c.PageNo = PageNo;
            c.OrderNo = OrderNo;
            c.MintYear = string.IsNullOrWhiteSpace(MintYear) ? null : MintYear;
            c.Mint = string.IsNullOrWhiteSpace(Mint) ? null : Mint;
            c.Quality = string.IsNullOrWhiteSpace(Quality) ? null : Quality;
            c.AnomalyDesc = string.IsNullOrWhiteSpace(AnomalyDesc) ? null : AnomalyDesc;
            c.IsAnomaly = IsAnomaly;
            if (IsDuplicate)
            {
                c.SourceKind = SourceKind.Duplicate;
            }
            else if (c.SourceKind is not (SourceKind.Match or SourceKind.Manual or SourceKind.Api))
            {
                // was a duplicate/variant/anomaly marker, now switched to etalon
                c.SourceKind = SourceKind.Match;
            }
            if (!string.IsNullOrWhiteSpace(Name))
            {
                var t = c.TypeId is int tid ? _db.GetCoinType(tid) : new CoinType();
                t.Name = Name;
                c.TypeId = _db.SaveCoinType(t);
            }
            _db.SaveCoin(c);
            Status = T("status.saved");
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
        }
    }

    // ---- annotation layer ----
    private void UndoAnnotation()
    {
        if (AnnotationItems.Count > 0)
            AnnotationItems.RemoveAt(AnnotationItems.Count - 1);
    }

    private void ClearAnnotations() => AnnotationItems.Clear();

    private void SaveAnnotations()
    {
        string side = SideNameRaw();
        var path = AnnotationLayer.Save(CoinId, side, AnnotationItems.ToList(), _settings.CoimgDir);
        var c = _db.GetCoin(CoinId);
        if (c is not null)
        {
            if (side == "obverse") c.ObverseLayer = path;
            else c.ReverseLayer = path;
            _db.SaveCoin(c);
        }
    }

    /// <summary>Load the saved annotation layer for the active side.</summary>
    private void LoadAnnotationLayer()
    {
        AnnotationItems.Clear();
        var c = _db.GetCoin(CoinId);
        if (c is null) return;
        string side = SideNameRaw();
        var path = side == "obverse" ? c.ObverseLayer : c.ReverseLayer;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        foreach (var item in AnnotationLayer.Load(path).Items)
            AnnotationItems.Add(item);
    }

    // ---- helpers ----
    private static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        // Load through a byte array: WPF caches BitmapImage by URI, so a
        // fresh UriSource for a rewritten file would show the stale image.
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(File.ReadAllBytes(path));
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource? LoadIfExists(string path) => LoadImage(path);
}
