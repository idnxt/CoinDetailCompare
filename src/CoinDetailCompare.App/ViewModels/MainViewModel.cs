using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoinDetailCompare.App.Controls;
using CoinDetailCompare.App.Models;
using CoinDetailCompare.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoinDetailCompare.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly BackendClient _backend;
    private readonly DbService _db;
    private readonly ICameraDevice _camera;
    private readonly LocalizationService _loc;
    private readonly Strings _strings;

    // ---- language / i18n ----
    private string _language = "ru";
    private string _annotationToolName = "";

    private string _status = "";
    private string _album = "";
    private int _pageNo;
    private int _orderNo;
    private double _rotation;
    private bool _isBusy;
    private bool _useGpu;
    private int _patchGrid = 16;
    private string? _obversePath;
    private string? _reversePath;
    private ImageSource? _obverseImage;
    private ImageSource? _reverseImage;
    private ImageSource? _etalonImage;
    private double _obversePct;
    private double _reversePct;
    private string _orientation = "";
    private string _numistaQuery = "";
    private string _numistaLink = "";
    private int? _currentCoinId;

    // ---- comparison view (active side + overlays) ----
    private CoinSide _compareSide = CoinSide.Obverse;
    private ImageSource? _compareImage;
    private string? _etalonObversePath;
    private string? _etalonReversePath;
    private List<EtalonCandidate> _etalonCandidates = new();
    private bool _hasEtalonCandidates;
    private bool _showHeatmap = true;
    private bool _showAnnotations = true;

    // ---- comparison layer (diff map + vector projection) ----
    private ImageSource? _diffImage;
    private bool _showDiff = true;
    private List<VectorPointItem> _vectorPoints = new();
    private bool _hasVectorPoints;
    private int? _etalonCoinId;

    // ---- anomaly / heatmap ----
    private ImageSource? _heatmapImage;
    private double _anomalyScore;
    private bool _isAnomaly;
    private string _anomalyStatus = "";

    // ---- annotation layer ----
    private AnnotationTool _annotationTool = AnnotationTool.Path;
    private string _annotationColor = "#FFFF3030";

    // ---- coin attribute editing ----
    private string _mintYear = "";
    private string _mint = "";
    private string _quality = "";
    private string _anomalyDesc = "";

    // ---- batch capture state ----
    private string _batchAlbum = "";
    private int _batchPage;
    private int _batchCount;
    private string _batchStatus = "";
    private BatchPhase _batchPhase = BatchPhase.Idle;
    private int _batchIndex;
    private readonly List<string> _batchObverse = new();
    private readonly List<string> _batchReverse = new();

    public MainViewModel(AppSettings settings, BackendClient backend, DbService db,
        ICameraDevice camera, LocalizationService loc, Strings strings)
    {
        _settings = settings;
        _backend = backend;
        _db = db;
        _camera = camera;
        _loc = loc;
        _strings = strings;
        _language = loc.CurrentLanguage;
        _annotationToolName = _strings["annotation.tool_line"];

        Languages = new() { "ru", "en" };
        AnnotationToolItems = new();
        RebuildAnnotationToolItems();

        ShootObverseCommand = new RelayCommand(async () => await ShootAsync(CoinSide.Obverse));
        ShootReverseCommand = new RelayCommand(async () => await ShootAsync(CoinSide.Reverse));
        SwapSidesCommand = new RelayCommand(SwapSides);
        SwapCompareCommand = new RelayCommand(SwapCompare);
        SearchCommand = new RelayCommand(async () => await SearchAsync());
        SwapEtalonCommand = new RelayCommand<EtalonCandidate>(SwapEtalon);
        SetupGpuCommand = new RelayCommand(async () => await SetupGpuAsync());
        SaveMatchCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Match));
        SaveDuplicateCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Duplicate));
        SaveVariantCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Variant));
        SaveAnomalyCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Anomaly));
        RebuildIndexCommand = new RelayCommand(async () => await RebuildIndexAsync());
        NumistaSearchCommand = new RelayCommand(async () => await NumistaSearchAsync());
        LoadObverseFileCommand = new RelayCommand(LoadObverseFile);
        LoadReverseFileCommand = new RelayCommand(LoadReverseFile);

        OpenCameraCommand = new RelayCommand(OpenCamera);
        StartBatchCommand = new RelayCommand(async () => await StartBatchAsync());
        CaptureNextCommand = new RelayCommand(async () => await CaptureNextAsync());
        CancelBatchCommand = new RelayCommand(CancelBatch);
        SaveBatchCommand = new RelayCommand(async () => await SaveBatchAsync());
        SaveAttributesCommand = new RelayCommand(async () => await SaveAttributesAsync());
        LoadTypeCommand = new RelayCommand(async () => await LoadTypeAsync());
        SaveTypeCommand = new RelayCommand(async () => await SaveTypeAsync());
        RunAnomalyCommand = new RelayCommand(async () => await RunAnomalyAsync());
        ValidateNumistaCommand = new RelayCommand(async () => await ValidateNumistaAsync());
        ApplyNumistaItemCommand = new RelayCommand(() => ApplyNumistaItem());
        UndoAnnotationCommand = new RelayCommand(UndoAnnotation);
        ClearAnnotationsCommand = new RelayCommand(ClearAnnotations);
        SaveAnnotationsCommand = new RelayCommand(async () => await SaveAnnotationsAsync());
        OpenCatalogCommand = new RelayCommand(OpenCatalog);

        _ = LoadReferencesAsync();
    }

    // ---- bindable properties ----
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Album { get => _album; set => SetProperty(ref _album, value); }
    public int PageNo { get => _pageNo; set => SetProperty(ref _pageNo, value); }
    public int OrderNo { get => _orderNo; set => SetProperty(ref _orderNo, value); }
    public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    // ---- comparison / anomaly options ----
    public bool UseGpu { get => _useGpu; set => SetProperty(ref _useGpu, value); }
    public int PatchGrid { get => _patchGrid; set => SetProperty(ref _patchGrid, value); }
    public List<int> PatchGridItems { get; } = new() { 8, 16, 32, 64, 128 };

    // ---- comparison view: active side + overlay visibility ----
    public CoinSide CompareSide { get => _compareSide; private set => SetProperty(ref _compareSide, value); }
    public ImageSource? CompareImage { get => _compareImage; private set => SetProperty(ref _compareImage, value); }
    public bool ShowHeatmap { get => _showHeatmap; set => SetProperty(ref _showHeatmap, value); }
    public bool ShowDiff { get => _showDiff; set => SetProperty(ref _showDiff, value); }
    public bool ShowAnnotations { get => _showAnnotations; set => SetProperty(ref _showAnnotations, value); }

    public string? ObversePath => _obversePath;
    public string? ReversePath => _reversePath;
    public ImageSource? ObverseImage { get => _obverseImage; private set => SetProperty(ref _obverseImage, value); }
    public ImageSource? ReverseImage { get => _reverseImage; private set => SetProperty(ref _reverseImage, value); }
    public ImageSource? EtalonImage { get => _etalonImage; set => SetProperty(ref _etalonImage, value); }
    public double ObversePct { get => _obversePct; set => SetProperty(ref _obversePct, value); }
    public double ReversePct { get => _reversePct; set => SetProperty(ref _reversePct, value); }
    public string Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }
    public string NumistaQuery { get => _numistaQuery; set => SetProperty(ref _numistaQuery, value); }
    public string NumistaLink { get => _numistaLink; set => SetProperty(ref _numistaLink, value); }
    public int? CurrentCoinId => _currentCoinId;

    // ---- etalon candidates (result of "find similar") ----
    public List<EtalonCandidate> EtalonCandidates
    {
        get => _etalonCandidates;
        private set
        {
            SetProperty(ref _etalonCandidates, value);
            HasEtalonCandidates = value is { Count: > 0 };
        }
    }
    public bool HasEtalonCandidates
    {
        get => _hasEtalonCandidates;
        private set => SetProperty(ref _hasEtalonCandidates, value);
    }

    // ---- attribute editing properties ----
    public string MintYear { get => _mintYear; set => SetProperty(ref _mintYear, value); }
    public string Mint { get => _mint; set => SetProperty(ref _mint, value); }
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }
    public string AnomalyDesc { get => _anomalyDesc; set => SetProperty(ref _anomalyDesc, value); }
    public ObservableCollection<string> QualityItems { get; } = new();
    public ObservableCollection<string> OrientationItems { get; } = new();
    public TypeEditor TypeEditor { get; } = new();
    public bool TypeLoaded => TypeEditor.Id > 0;

    // ---- comparison layer (diff image + 2-D vector projection) ----
    public ImageSource? DiffImage { get => _diffImage; private set => SetProperty(ref _diffImage, value); }
    public List<VectorPointItem> VectorPoints
    {
        get => _vectorPoints;
        private set
        {
            SetProperty(ref _vectorPoints, value);
            HasVectorPoints = value is { Count: > 0 };
        }
    }
    public bool HasVectorPoints
    {
        get => _hasVectorPoints;
        private set => SetProperty(ref _hasVectorPoints, value);
    }

    // ---- anomaly / heatmap ----
    public ImageSource? HeatmapImage { get => _heatmapImage; private set => SetProperty(ref _heatmapImage, value); }
    public double AnomalyScore { get => _anomalyScore; private set => SetProperty(ref _anomalyScore, value); }
    public bool IsAnomaly { get => _isAnomaly; private set => SetProperty(ref _isAnomaly, value); }
    public string AnomalyStatus { get => _anomalyStatus; private set => SetProperty(ref _anomalyStatus, value); }

    // ---- annotation layer ----
    public ObservableCollection<AnnotationItem> AnnotationItems { get; } = new();
    public AnnotationTool AnnotationTool
    {
        get => _annotationTool;
        set => SetProperty(ref _annotationTool, value);
    }
    public string AnnotationColor
    {
        get => _annotationColor;
        set => SetProperty(ref _annotationColor, value);
    }
    public List<AnnotationTool> AnnotationTools { get; } = Enum.GetValues<AnnotationTool>().ToList();
    public ObservableCollection<string> AnnotationToolItems { get; }
    public string AnnotationToolName
    {
        get => _annotationToolName;
        set
        {
            if (SetProperty(ref _annotationToolName, value))
            {
                // order in AnnotationToolItems: line, ellipse, arrow
                AnnotationTool = AnnotationToolItems.IndexOf(value) switch
                {
                    1 => AnnotationTool.Ellipse,
                    2 => AnnotationTool.Arrow,
                    _ => AnnotationTool.Path,
                };
            }
        }
    }

    // ---- language / i18n ----
    public ObservableCollection<string> Languages { get; }
    public string Language
    {
        get => _language;
        set
        {
            if (!SetProperty(ref _language, value))
                return;
            _strings.SetLanguage(value);
            RebuildAnnotationToolItems();
            OnPropertyChanged(nameof(T)); // status strings refreshed by consumers via T()
        }
    }

    private static string ToolKeyFor(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Path => "annotation.tool_line",
        AnnotationTool.Ellipse => "annotation.tool_ellipse",
        AnnotationTool.Arrow => "annotation.tool_arrow",
        _ => "annotation.tool_line",
    };

    private static AnnotationTool ToolFor(string key) => key switch
    {
        "annotation.tool_ellipse" => AnnotationTool.Ellipse,
        "annotation.tool_arrow" => AnnotationTool.Arrow,
        _ => AnnotationTool.Path,
    };

    private void RebuildAnnotationToolItems()
    {
        var current = ToolKeyFor(AnnotationTool);
        AnnotationToolItems.Clear();
        foreach (var key in new[]
                 {
                     "annotation.tool_line",
                     "annotation.tool_ellipse",
                     "annotation.tool_arrow",
                 })
        {
            AnnotationToolItems.Add(_strings[key]);
        }
        _annotationToolName = _strings[current];
        SetProperty(ref _annotationToolName, _annotationToolName);
    }

    // ---- batch properties ----
    public string BatchAlbum { get => _batchAlbum; set => SetProperty(ref _batchAlbum, value); }
    public int BatchPage { get => _batchPage; set => SetProperty(ref _batchPage, value); }
    public int BatchCount { get => _batchCount; set => SetProperty(ref _batchCount, value); }
    public string BatchStatus { get => _batchStatus; set => SetProperty(ref _batchStatus, value); }
    public BatchPhase BatchPhase { get => _batchPhase; set => SetProperty(ref _batchPhase, value); }
    public int BatchIndex => _batchIndex;
    public bool BatchActive => _batchPhase is BatchPhase.Obverse or BatchPhase.Reverse;
    public List<BatchResult> BatchResults { get; } = new();

    // ---- commands ----
    public RelayCommand ShootObverseCommand { get; }
    public RelayCommand ShootReverseCommand { get; }
    public RelayCommand SwapSidesCommand { get; }
    public RelayCommand SwapCompareCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand<EtalonCandidate> SwapEtalonCommand { get; }
    public RelayCommand SetupGpuCommand { get; }
    public RelayCommand SaveMatchCommand { get; }
    public RelayCommand SaveDuplicateCommand { get; }
    public RelayCommand SaveVariantCommand { get; }
    public RelayCommand SaveAnomalyCommand { get; }
    public RelayCommand RebuildIndexCommand { get; }
    public RelayCommand NumistaSearchCommand { get; }
    public RelayCommand LoadObverseFileCommand { get; }
    public RelayCommand LoadReverseFileCommand { get; }
    public RelayCommand OpenCameraCommand { get; }
    public RelayCommand StartBatchCommand { get; }
    public RelayCommand CaptureNextCommand { get; }
    public RelayCommand CancelBatchCommand { get; }
    public RelayCommand SaveBatchCommand { get; }
    public RelayCommand SaveAttributesCommand { get; }
    public RelayCommand LoadTypeCommand { get; }
    public RelayCommand SaveTypeCommand { get; }
    public RelayCommand RunAnomalyCommand { get; }
    public RelayCommand UndoAnnotationCommand { get; }
    public RelayCommand ClearAnnotationsCommand { get; }
    public RelayCommand SaveAnnotationsCommand { get; }
    public RelayCommand ValidateNumistaCommand { get; }
    public RelayCommand ApplyNumistaItemCommand { get; }
    public RelayCommand OpenCatalogCommand { get; }

    public string T(string key) => _loc.T(key);

    // ==================== camera ====================
    public bool IsCameraOpen => _camera.IsOpen;

    public bool TryGetCameraFrame(out System.Windows.Media.Imaging.BitmapSource? frame)
        => _camera.TryGetFrame(out frame);

    private CaptureWindow? _captureWindow;

    private void OpenCamera()
    {
        // Bring the capture window forward if it is already open.
        if (_captureWindow is { IsLoaded: true })
        {
            _captureWindow.Activate();
            return;
        }
        if (!_camera.IsOpen && !_camera.Open())
        {
            Status = T("capture.camera_offline");
            return;
        }
        Status = $"{_camera.Description} - open";

        var win = new CaptureWindow(this)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        win.Closed += (_, _) => _captureWindow = null;
        _captureWindow = win;
        win.Show();
    }

    // ==================== single-shot capture ====================
    private async Task ShootAsync(CoinSide side)
    {
        IsBusy = true;
        try
        {
            string? raw;
            if (_camera.IsOpen && _camera.TryGetFrame(out var frame) && frame != null)
            {
                raw = SaveFrame(frame, side);
            }
            else
            {
                Status = T("capture.camera_offline");
                IsBusy = false;
                return;
            }

            var resp = await PreprocessAsync(raw, side);
            if (resp is { ok: true } && !string.IsNullOrEmpty(resp.crop_path))
            {
                if (side == CoinSide.Obverse)
                {
                    _obversePath = resp.crop_path;
                    ObverseImage = LoadImage(resp.crop_path);
                }
                else
                {
                    _reversePath = resp.crop_path;
                    ReverseImage = LoadImage(resp.crop_path);
                }
                if (side == CompareSide)
                    CompareImage = LoadImage(resp.crop_path);
                Status = $"crop: {resp.shape_detected}, {resp.orientation_applied_deg:F1}deg";
            }
            else
            {
                Status = resp?.error ?? T("status.error");
            }
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

    /// <summary>
    /// Sends the raw photo to the backend for preprocessing.
    /// If a matched etalon photo exists on disk, it is passed for auto-alignment.
    /// </summary>
    private async Task<PreprocessResponse?> PreprocessAsync(string rawPath, CoinSide side)
    {
        string? etalonPath = null;
        if (_currentCoinId is int cid)
        {
            var etalon = _db.GetCoin(cid);
            etalonPath = side == CoinSide.Obverse ? etalon?.ObversePhoto : etalon?.ReversePhoto;
            if (!string.IsNullOrEmpty(etalonPath) && !File.Exists(etalonPath))
                etalonPath = null;
        }

        var req = new PreprocessRequest(
            raw_path: rawPath,
            side: side == CoinSide.Obverse ? "obverse" : "reverse",
            target_size: _settings.TargetSize,
            orientation_deg: Rotation,
            etalon_path: etalonPath);
        return await _backend.PreprocessAsync(req);
    }

    private string SaveFrame(BitmapSource frame, CoinSide side)
    {
        var name = $"raw_{side}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var path = Path.Combine(_settings.CoimgDir, "tmp", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(frame));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    // ---- manual file loading (dev/testing without camera) ----
    private void LoadObverseFile()
    {
        var p = PickImage();
        if (p == null) return;
        _obversePath = p;
        ObverseImage = LoadImage(p);
        if (CompareSide == CoinSide.Obverse)
            CompareImage = LoadImage(p);
    }

    private void LoadReverseFile()
    {
        var p = PickImage();
        if (p == null) return;
        _reversePath = p;
        ReverseImage = LoadImage(p);
        if (CompareSide == CoinSide.Reverse)
            CompareImage = LoadImage(p);
    }

    private static string? PickImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    // ==================== batch capture ====================
    /// <summary>
    /// Batch mode: album/page are given BEFORE the run. All obverses are
    /// captured first, then all reverses in the same order. Coins are
    /// numbered 1..N by photo order.
    /// </summary>
    private async Task StartBatchAsync()
    {
        if (BatchCount < 1)
        {
            BatchStatus = T("status.error");
            return;
        }
        if (!_camera.IsOpen)
            OpenCamera();
        if (!_camera.IsOpen)
        {
            BatchStatus = T("capture.camera_offline");
            return;
        }

        _batchObverse.Clear();
        _batchReverse.Clear();
        BatchResults.Clear();
        _batchIndex = 0;
        BatchPhase = BatchPhase.Obverse;
        BatchStatus = $"phase: obverse, 0/{BatchCount}";
        await CaptureNextAsync();
    }

    private async Task CaptureNextAsync()
    {
        if (!BatchActive)
            return;
        IsBusy = true;
        try
        {
            CoinSide side = BatchPhase == BatchPhase.Obverse ? CoinSide.Obverse : CoinSide.Reverse;
            if (!_camera.TryGetFrame(out var frame) || frame == null)
            {
                BatchStatus = T("capture.camera_offline");
                return;
            }

            var raw = SaveFrame(frame, side);
            var resp = await PreprocessAsync(raw, side);
            if (resp is not { ok: true } || string.IsNullOrEmpty(resp.crop_path))
            {
                // Retake: do not advance the order number.
                BatchStatus = $"retry: {resp?.error ?? T("status.error")}";
                return;
            }

            _batchIndex++;
            int orderNo = _batchIndex;
            if (BatchPhase == BatchPhase.Obverse)
                _batchObverse.Add(resp.crop_path);
            else
                _batchReverse.Add(resp.crop_path);

            if (BatchPhase == BatchPhase.Obverse)
            {
                ObverseImage = LoadImage(resp.crop_path);
                if (_batchIndex >= BatchCount)
                {
                    BatchPhase = BatchPhase.Reverse;
                    _batchIndex = 0;
                    BatchStatus = "phase: reverse, 0/" + BatchCount;
                    await CaptureNextAsync();
                    return;
                }
                BatchStatus = $"phase: obverse, {_batchIndex}/{BatchCount}";
            }
            else
            {
                ReverseImage = LoadImage(resp.crop_path);
                if (_batchIndex >= BatchCount)
                {
                    BatchPhase = BatchPhase.Done;
                    BatchStatus = "capturing done - matching...";
                    await MatchBatchAsync();
                    return;
                }
                BatchStatus = $"phase: reverse, {_batchIndex}/{BatchCount}";
            }
            _ = orderNo; // order number == capture order, stored with results
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelBatch()
    {
        _batchObverse.Clear();
        _batchReverse.Clear();
        BatchResults.Clear();
        _batchIndex = 0;
        BatchPhase = BatchPhase.Idle;
        BatchStatus = "";
    }

    /// <summary>After both phases: pair obverse[i]+reverse[i] and run search for each pair.</summary>
    private async Task MatchBatchAsync()
    {
        for (int i = 0; i < BatchResults.Count || i < BatchCount; i++)
        {
            if (i >= _batchObverse.Count || i >= _batchReverse.Count)
                break;
            var result = new BatchResult
            {
                OrderNo = i + 1,
                ObversePath = _batchObverse[i],
                ReversePath = _batchReverse[i],
            };
            BatchResults.Add(result);

            try
            {
                var req = new SearchRequest(
                    crop_path: _batchObverse[i],
                    side: "obverse",
                    obverse_path: _batchObverse[i],
                    reverse_path: _batchReverse[i],
                    candidate_type_id: null,
                    etalon_quality: null,
                    topk: 10);
                var resp = await _backend.SearchAsync(req);
                if (resp is { candidates: { Count: > 0 } })
                {
                    var top = resp.candidates[0];
                    result.TopCoinId = top.coin_id;
                    result.ObversePct = Math.Round(top.obverse_pct, 1);
                    result.ReversePct = Math.Round(top.reverse_pct, 1);
                    result.Orientation = top.orientation;
                    result.Status = $"{top.overall_pct:F1}%";
                }
                else
                {
                    result.Status = T("actions.not_determined");
                }
            }
            catch
            {
                result.Status = T("status.error");
            }
        }
        BatchStatus = $"matched {BatchResults.Count} coins";
    }

    /// <summary>Save every batch coin: embed + persist crops, then write DB rows.</summary>
    private async Task SaveBatchAsync()
    {
        int saved = 0;
        for (int i = 0; i < BatchResults.Count; i++)
        {
            try
            {
                var r = BatchResults[i];
                var id = _db.NextCoinId();
                string? ovPhoto = r.ObversePath, revPhoto = r.ReversePath, ovVec = null, revVec = null;
                var embed = await _backend.EmbedAsync(new EmbedRequest(id, r.ObversePath, r.ReversePath));
                if (embed is { ok: true })
                {
                    foreach (var s in embed.sides)
                    {
                        if (s.side == "obverse") { ovPhoto = s.photo_path; ovVec = s.vector_path; }
                        else { revPhoto = s.photo_path; revVec = s.vector_path; }
                    }
                }
                var coin = new Coin
                {
                    Id = id,
                    AlbumName = string.IsNullOrEmpty(BatchAlbum) ? Album : BatchAlbum,
                    PageNo = BatchPage,
                    OrderNo = r.OrderNo,
                    ObversePhoto = ovPhoto,
                    ReversePhoto = revPhoto,
                    ObverseVector = ovVec,
                    ReverseVector = revVec,
                    SourceKind = r.TopCoinId != null ? SourceKind.Match : SourceKind.Manual,
                    EtalonId = r.TopCoinId,
                };
                _db.SaveCoin(coin);
                saved++;
            }
            catch (System.Exception)
            {
                // skip this batch coin, keep saving the rest
            }
        }
        BatchStatus = $"{saved} saved";
    }

    // ==================== search ====================
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(_obversePath) && string.IsNullOrEmpty(_reversePath))
        {
            Status = T("status.error");
            return;
        }
        IsBusy = true;
        try
        {
            var req = new SearchRequest(
                crop_path: _obversePath ?? _reversePath!,
                side: _obversePath != null ? "obverse" : "reverse",
                obverse_path: _obversePath,
                reverse_path: _reversePath,
                candidate_type_id: null,
                etalon_quality: null,
                topk: 10);
            var resp = await _backend.SearchAsync(req);
            if (resp is { candidates: { Count: > 0 } })
            {
                var top = resp.candidates[0];
                ObversePct = Math.Round(top.obverse_pct, 1);
                ReversePct = Math.Round(top.reverse_pct, 1);
                Orientation = top.orientation;
                ApplyCoinAttributes(top.coin_id);
                _etalonObversePath = top.etalon_photo.obverse;
                _etalonReversePath = top.etalon_photo.reverse;
                RefreshCompareImages();
                EtalonCandidates = BuildCandidates(resp.candidates);
                _etalonCoinId = top.coin_id;
                Status = $"top match: {top.coin_id} ({top.overall_pct:F1}%)";
                _ = RunCompareAsync();
            }
            else
            {
                EtalonCandidates = new List<EtalonCandidate>();
                _etalonCoinId = null;
                Status = T("actions.not_determined");
            }
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

    private void SwapSides()
    {
        (_obversePath, _reversePath) = (_reversePath, _obversePath);
        (ObverseImage, ReverseImage) = (ReverseImage, ObverseImage);
        Status = T("actions.swap_sides");
    }

    /// <summary>Maps search results to the etalon-candidate list model:
    /// sorted by overall match, up to 5 entries, numbered 1..5 (1 = closest).</summary>
    private List<EtalonCandidate> BuildCandidates(List<CandidateDto> candidates)
    {
        return candidates
            .Where(c => c.etalon_photo is not null &&
                        (c.etalon_photo.obverse is not null || c.etalon_photo.reverse is not null))
            .OrderByDescending(c => c.overall_pct)
            .Take(5)
            .Select((c, i) => new EtalonCandidate
            {
                CoinId = c.coin_id,
                TypeId = c.type_id,
                Quality = c.quality,
                ObversePct = c.obverse_pct,
                ReversePct = c.reverse_pct,
                OverallPct = c.overall_pct,
                Orientation = c.orientation,
                EtalonObverse = c.etalon_photo.obverse,
                EtalonReverse = c.etalon_photo.reverse,
                Label = BuildCandidateLabel(i + 1, c),
            })
            .ToList();
    }

    /// <summary>Human-readable line: rank, record name, year, quality, match %.</summary>
    private string BuildCandidateLabel(int rank, CandidateDto c)
    {
        var coin = _db.GetCoin(c.coin_id);
        var name = c.type_id is int tid ? _db.GetCoinType(tid)?.Name : null;
        if (string.IsNullOrWhiteSpace(name))
            name = coin is not null && !string.IsNullOrWhiteSpace(coin.AlbumName)
                ? $"{coin.AlbumName} p.{coin.PageNo} №{coin.OrderNo}"
                : $"#{c.coin_id}";
        var parts = new List<string> { $"{rank}." };
        parts.Add(name!);
        if (!string.IsNullOrWhiteSpace(coin?.MintYear)) parts.Add(coin!.MintYear!);
        if (!string.IsNullOrWhiteSpace(c.quality)) parts.Add(c.quality!);
        parts.Add($"{c.overall_pct:F1}%");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Swaps the given candidate in as the etalon: its photos become the
    /// comparison reference, the match percents / orientation are updated and
    /// the coin-attributes panel loads the candidate's catalog record.
    /// </summary>
    private void SwapEtalon(EtalonCandidate? candidate)
    {
        if (candidate is null) return;
        _etalonObversePath = candidate.EtalonObverse;
        _etalonReversePath = candidate.EtalonReverse;
        ObversePct = Math.Round(candidate.ObversePct, 1);
        ReversePct = Math.Round(candidate.ReversePct, 1);
        Orientation = candidate.Orientation;
        _etalonCoinId = candidate.CoinId;
        ApplyCoinAttributes(candidate.CoinId);
        RefreshCompareImages();
        Status = string.Format(T("compare.etalon_set"), candidate.CoinId, candidate.OverallPct.ToString("F1"));
        _ = RunCompareAsync();
    }

    /// <summary>
    /// Comparison layer for the active side against the current etalon:
    /// rotation-aligned diff image (red overlay) plus the 2-D projection of
    /// the search vectors (query coin + etalon candidates).
    /// </summary>
    private async Task RunCompareAsync()
    {
        var side = CompareSide == CoinSide.Obverse ? "obverse" : "reverse";
        var crop = side == "obverse" ? _obversePath : _reversePath;
        if (string.IsNullOrEmpty(crop) || _etalonCoinId is not int eid)
        {
            DiffImage = null;
            VectorPoints = new List<VectorPointItem>();
            return;
        }
        try
        {
            var resp = await _backend.CompareAsync(new CompareRequest(
                crop_path: crop,
                side: side,
                etalon_coin_id: eid,
                coin_id: _currentCoinId,
                candidate_ids: EtalonCandidates.Select(c => c.CoinId).Distinct().ToList()));
            if (resp is not { ok: true })
            {
                Status = $"{T("status.error")}: {resp?.error ?? "no response"}";
                return;
            }
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
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
        }
    }

    /// <summary>
    /// In-app GPU setup: confirmation dialog (sizes, OK/Cancel), then runs
    /// scripts/setup_gpu.py, which installs EVERYTHING into &lt;root&gt;/env —
    /// full Python 3.12 into env/python (silent download+install if missing),
    /// the venv env/venv with all requirements, and PyTorch (cu128 when the
    /// NVIDIA driver is present). Streams the output to a log window, then
    /// restarts the backend and reports the device from /health.
    /// </summary>
    private async Task SetupGpuAsync()
    {
        if (IsBusy) return;

        var root = Path.GetFullPath(Path.Combine(_settings.CoimgDir, ".."));
        var envDir = Path.Combine(root, "env");
        var answer = System.Windows.MessageBox.Show(
            string.Format(T("gpu.confirm"), envDir, T("gpu.sizes")),
            T("app.title"),
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Information);
        if (answer != System.Windows.MessageBoxResult.OK) return;

        IsBusy = true;
        GpuSetupWindow? win = null;
        try
        {
            var py = await FindBootstrapPythonAsync(root, envDir);
            var script = Path.Combine(root, "scripts", "setup_gpu.py");

            win = new GpuSetupWindow { Owner = System.Windows.Application.Current?.MainWindow };
            win.Show();
            win.SetStatus(T("gpu.starting"));
            win.AppendLine($"python: {py}");
            Status = T("gpu.starting");

            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = py,
                    Arguments = $"\"{script}\"",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            void OnLine(object? sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                if (e.Data is not null)
                {
                    try { win!.AppendLine(e.Data); } catch { /* window closed */ }
                }
            }
            proc.OutputDataReceived += OnLine;
            proc.ErrorDataReceived += OnLine;
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            var exited = new TaskCompletionSource<bool>();
            proc.Exited += (_, _) => exited.TrySetResult(true);
            await exited.Task;
            var code = proc.ExitCode;

            if (code == 0)
            {
                Status = T("gpu.restarting");
                try { win.SetStatus(T("gpu.restarting")); } catch { }
                await RestartBackendAsync();
                HealthDto? h = null;
                for (var i = 0; i < 60; i++)
                {
                    await Task.Delay(2000);
                    try { h = await _backend.HealthAsync(); } catch { h = null; }
                    if (h is not null) break;
                }
                if (h is not null)
                {
                    Status = string.Format(T("gpu.ready"), h.device);
                    try { win.SetStatus(Status); } catch { }
                }
                else
                {
                    Status = T("gpu.restart_failed");
                    try { win.SetStatus(T("gpu.restart_failed")); } catch { }
                }
            }
            else
            {
                Status = T("gpu.failed");
                try { win.SetStatus(T("gpu.failed")); } catch { }
            }
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
            try { win?.SetStatus(Status); } catch { }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Restarts the local backend via scripts/restart_backend.ps1.</summary>
    private Task RestartBackendAsync()
    {
        var root = Path.GetFullPath(Path.Combine(_settings.CoimgDir, ".."));
        var ps = Path.Combine(root, "scripts", "restart_backend.ps1");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps}\" {_settings.BackendPort}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var p = System.Diagnostics.Process.Start(psi);
        return p is null ? Task.CompletedTask : p.WaitForExitAsync();
    }

    /// <summary>
    /// Finds a python to run setup_gpu.py, in priority order:
    /// env\python (installed by a previous run of the setup itself) →
    /// backend .venv → python on PATH. If none exists, downloads a small
    /// embeddable Python (~25 MB, stdlib only — enough to run the setup
    /// script) into env\_bootstrap; the script then installs the full
    /// Python into env\python.
    /// </summary>
    private async Task<string> FindBootstrapPythonAsync(string root, string envDir)
    {
        var envPy = Path.Combine(envDir, "python", "python.exe");
        if (File.Exists(envPy)) return envPy;

        var venvPy = Path.Combine(root, "src", "CoinDetailCompare.Backend", ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPy)) return venvPy;

        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "where.exe", "python")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                var line = (await p.StandardOutput.ReadLineAsync())?.Trim();
                await p.WaitForExitAsync();
                if (!string.IsNullOrWhiteSpace(line) && File.Exists(line)) return line;
            }
        }
        catch { /* no python on PATH */ }

        const string zipUrl =
            "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";
        var dir = Path.Combine(envDir, "_bootstrap");
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "python-embed.zip");
        if (!File.Exists(zip))
        {
            using var http = new System.Net.Http.HttpClient();
            using var stream = await http.GetStreamAsync(zipUrl);
            using var fs = File.Create(zip);
            await stream.CopyToAsync(fs);
        }
        ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
        return Path.Combine(dir, "python.exe");
    }

    /// <summary>
    /// Toggle the comparison side: obverse ↔ reverse. The photos of the
    /// current coin and the etalon, the heatmap and the saved annotation
    /// layer are reloaded for the newly active side.
    /// </summary>
    private void SwapCompare()
    {
        CompareSide = CompareSide == CoinSide.Obverse ? CoinSide.Reverse : CoinSide.Obverse;
        RefreshCompareImages();
        LoadAnnotationLayer();
        Status = T("compare.swap");
        _ = RunCompareAsync();
    }

    /// <summary>Open the saved-coins catalog window (non-modal).</summary>
    private void OpenCatalog()
    {
        var win = new CoinDetailCompare.App.Views.CatalogWindow(_settings, _backend, _db, _loc);
        win.Owner = System.Windows.Application.Current?.MainWindow;
        win.Show();
    }

    /// <summary>Reload the photos (current + etalon) and heatmap for CompareSide.</summary>
    private void RefreshCompareImages()
    {
        var path = CompareSide == CoinSide.Obverse ? _obversePath : _reversePath;
        CompareImage = path is not null && File.Exists(path) ? LoadImage(path) : null;

        var etalonPath = CompareSide == CoinSide.Obverse ? _etalonObversePath : _etalonReversePath;
        EtalonImage = etalonPath is not null && File.Exists(etalonPath) ? LoadImage(etalonPath) : null;

        HeatmapImage = TryLoadHeatmap();
    }

    /// <summary>Heatmap file written by the backend for the active side, if any.</summary>
    private ImageSource? TryLoadHeatmap()
    {
        if (_currentCoinId is not int cid) return null;
        string side = CompareSide == CoinSide.Obverse ? "obverse" : "reverse";
        var path = Path.Combine(_settings.CoimgDir, $"{cid}_{side}_heatmap.png");
        return File.Exists(path) ? LoadImage(path) : null;
    }

    // ==================== save ====================
    private async Task SaveAsync(SourceKind kind)
    {
        if (string.IsNullOrEmpty(_obversePath) && string.IsNullOrEmpty(_reversePath))
        {
            Status = T("status.error");
            return;
        }
        int id;
        try
        {
            id = _db.NextCoinId();
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
            return;
        }
        string? ovPhoto = _obversePath, revPhoto = _reversePath, ovVec = null, revVec = null;
        try
        {
            var embed = await _backend.EmbedAsync(new EmbedRequest(id, _obversePath, _reversePath));
            if (embed is { ok: true })
            {
                foreach (var s in embed.sides)
                {
                    if (s.side == "obverse") { ovPhoto = s.photo_path; ovVec = s.vector_path; }
                    else { revPhoto = s.photo_path; revVec = s.vector_path; }
                }
            }
            else
            {
                Status = embed?.error ?? T("status.error");
                return;
            }
            var coin = new Coin
            {
                Id = id,
                MintYear = null,
                Quality = null,
                Price = 0,
                AlbumName = Album,
                PageNo = PageNo,
                OrderNo = OrderNo,
                ObversePhoto = ovPhoto,
                ReversePhoto = revPhoto,
                ObverseVector = ovVec,
                ReverseVector = revVec,
                SourceKind = kind,
                IsAnomaly = kind == SourceKind.Anomaly,
                EtalonId = _currentCoinId,
            };
            _db.SaveCoin(coin);
            _currentCoinId = id;
            Status = T("status.saved");
        }
        catch (System.Exception ex)
        {
            Status = $"{T("status.error")}: {ex.Message}";
        }
    }

    private async Task RebuildIndexAsync()
    {
        IsBusy = true;
        try
        {
            var counts = await _backend.RebuildIndexAsync();
            Status = counts is { }
                ? $"index: obverse={counts["obverse"]}, reverse={counts["reverse"]}"
                : T("status.error");
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

    // ==================== numista ====================
    private NumistaItemDto? _numistaSelected;
    public ObservableCollection<NumistaItemDto> NumistaResults { get; } = new();
    public NumistaItemDto? NumistaSelected
    {
        get => _numistaSelected;
        set => SetProperty(ref _numistaSelected, value);
    }

    private async Task NumistaSearchAsync()
    {
        IsBusy = true;
        try
        {
            var resp = await _backend.NumistaSearchAsync(new NumistaSearchRequest(NumistaQuery, 20));
            NumistaResults.Clear();
            if (resp is { error: not null })
            {
                Status = resp.error;
                return;
            }
            foreach (var item in resp?.items ?? new List<NumistaItemDto>())
                NumistaResults.Add(item);
            Status = $"{NumistaResults.Count} items";
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

    /// <summary>Validate the manually entered Numista link and fetch its title.</summary>
    private async Task ValidateNumistaAsync()
    {
        if (string.IsNullOrWhiteSpace(NumistaLink))
        {
            Status = T("status.error");
            return;
        }
        IsBusy = true;
        try
        {
            var resp = await _backend.ParseNumistaLinkAsync(NumistaLink, fetchMeta: true);
            if (resp is null)
            {
                Status = T("status.error");
                return;
            }
            TypeEditor.NumistaUrl = NumistaLink;
            if (!string.IsNullOrEmpty(resp.title) && string.IsNullOrWhiteSpace(TypeEditor.Name))
                TypeEditor.Name = resp.title;
            Status = $"N#{resp.numista_id} {resp.kind} — {resp.title ?? "?"}";
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

    /// <summary>Apply the selected search result to the current coin / type card.</summary>
    private void ApplyNumistaItem()
    {
        if (NumistaSelected is null)
        {
            Status = T("status.error");
            return;
        }
        NumistaLink = NumistaSelected.url ?? NumistaLink;
        TypeEditor.NumistaUrl = NumistaSelected.url;
        if (string.IsNullOrWhiteSpace(TypeEditor.Name) && !string.IsNullOrEmpty(NumistaSelected.title))
            TypeEditor.Name = NumistaSelected.title;
        if (_currentCoinId is int cid)
        {
            var c = _db.GetCoin(cid);
            if (c is not null)
            {
                c.NumistaUrl = NumistaSelected.url;
                _db.SaveCoin(c);
            }
        }
        Status = T("status.saved");
    }

    // ==================== references & attributes ====================
    private async Task LoadReferencesAsync()
    {
        try
        {
            var refs = await _backend.ReferencesAsync();
            if (refs is null) return;
            foreach (var r in refs.items)
            {
                if (r.kind == "quality")
                    QualityItems.Add(string.IsNullOrEmpty(r.ru) ? r.code : r.ru);
                else if (r.kind == "orientation")
                    OrientationItems.Add(string.IsNullOrEmpty(r.ru) ? r.code : r.ru);
            }
        }
        catch
        {
            // backend offline: dropdowns stay empty
        }
    }

    private void ApplyCoinAttributes(int coinId)
    {
        var c = _db.GetCoin(coinId);
        if (c is null) return;
        MintYear = c.MintYear ?? "";
        Mint = c.Mint ?? "";
        Quality = c.Quality ?? "";
        AnomalyDesc = c.AnomalyDesc ?? "";
        NumistaLink = c.NumistaUrl ?? NumistaLink;
        _currentCoinId = coinId;
        LoadAnnotationLayer();
    }

    private async Task SaveAttributesAsync()
    {
        if (_currentCoinId is not int cid)
        {
            Status = T("status.error");
            return;
        }
        var c = _db.GetCoin(cid) ?? new Coin { Id = cid };
        c.MintYear = string.IsNullOrWhiteSpace(MintYear) ? null : MintYear;
        c.Mint = string.IsNullOrWhiteSpace(Mint) ? null : Mint;
        c.Quality = string.IsNullOrWhiteSpace(Quality) ? null : Quality;
        c.AnomalyDesc = string.IsNullOrWhiteSpace(AnomalyDesc) ? null : AnomalyDesc;
        c.NumistaUrl = string.IsNullOrWhiteSpace(NumistaLink) ? null : NumistaLink;
        _db.SaveCoin(c);
        Status = T("status.saved");
    }

    // ==================== anomaly ====================
    private async Task RunAnomalyAsync()
    {
        string? path = CompareSide == CoinSide.Obverse ? _obversePath : _reversePath;
        if (path is null)
        {
            Status = T("status.error");
            return;
        }
        CoinSide side = CompareSide;
        var refs = new List<int>();
        if (_currentCoinId is int eid)
            refs.Add(eid); // etalon as the normal reference
        IsBusy = true;
        try
        {
            var resp = await _backend.AnomalyAsync(new AnomalyRequest(
                crop_path: path,
                side: side == CoinSide.Obverse ? "obverse" : "reverse",
                reference_ids: refs,
                heatmap_size: 128,
                threshold: null,
                coin_id: _currentCoinId,
                device: UseGpu ? "gpu" : "cpu",
                patch_grid: PatchGrid));
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
            HeatmapImage = resp.heatmap_path is not null && File.Exists(resp.heatmap_path)
                ? LoadImage(resp.heatmap_path)
                : TryLoadHeatmap();
            // Show the rotation-normalized coin (aligned to the etalon).
            if (resp.normalized_path is not null && File.Exists(resp.normalized_path))
                CompareImage = LoadImage(resp.normalized_path);
        }
        catch (System.Exception ex)
        {
            // Timeout / backend hiccup: report, don't crash the UI.
            AnomalyStatus = $"{T("status.error")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==================== annotation layer ====================
    private void UndoAnnotation()
    {
        if (AnnotationItems.Count > 0)
            AnnotationItems.RemoveAt(AnnotationItems.Count - 1);
    }

    private void ClearAnnotations()
    {
        AnnotationItems.Clear();
    }

    private async Task SaveAnnotationsAsync()
    {
        if (_currentCoinId is not int cid)
        {
            Status = T("status.error");
            return;
        }
        string side = CompareSide == CoinSide.Obverse ? "obverse" : "reverse";
        var path = AnnotationLayer.Save(cid, side, AnnotationItems.ToList(), _settings.CoimgDir);
        var c = _db.GetCoin(cid);
        if (c != null)
        {
            if (side == "obverse") c.ObverseLayer = path;
            else c.ReverseLayer = path;
            _db.SaveCoin(c);
        }
        Status = T("status.saved");
    }

    /// <summary>Load the saved annotation layer for the current coin (matching side).</summary>
    private void LoadAnnotationLayer()
    {
        AnnotationItems.Clear();
        if (_currentCoinId is not int cid) return;
        var c = _db.GetCoin(cid);
        if (c is null) return;
        string side = CompareSide == CoinSide.Obverse ? "obverse" : "reverse";
        var path = side == "obverse" ? c.ObverseLayer : c.ReverseLayer;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var layer = AnnotationLayer.Load(path);
        foreach (var item in layer.Items)
            AnnotationItems.Add(item);
    }

    // ==================== type card ====================
    private async Task LoadTypeAsync()
    {
        if (_currentCoinId is not int cid)
        {
            TypeEditor.Clear();
            return;
        }
        var c = _db.GetCoin(cid);
        var typeId = c?.TypeId;
        if (typeId is null)
        {
            // try the etalon's type
            typeId = c?.EtalonId is int eid ? _db.GetCoin(eid)?.TypeId : null;
        }
        var t = typeId is int tid ? _db.GetCoinType(tid) : null;
        TypeEditor.LoadFrom(t);
    }

    private async Task SaveTypeAsync()
    {
        if (string.IsNullOrWhiteSpace(TypeEditor.Name) && TypeEditor.Id == 0)
        {
            Status = T("status.error");
            return;
        }
        var id = _db.SaveCoinType(TypeEditor.ToCoinType());
        if (_currentCoinId is int cid)
        {
            var c = _db.GetCoin(cid) ?? new Coin { Id = cid };
            c.TypeId = id;
            _db.SaveCoin(c);
        }
        TypeEditor.Id = id;
        Status = T("status.saved");
    }

    // ==================== helpers ====================
    private static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        // Load through a byte array: WPF caches BitmapImage by URI, so a
        // fresh UriSource for a rewritten file (e.g. a new heatmap under
        // the same name) would show the stale image.
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(File.ReadAllBytes(path));
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
