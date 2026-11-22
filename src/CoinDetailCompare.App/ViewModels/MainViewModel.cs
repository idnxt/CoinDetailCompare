using System.Collections.ObjectModel;
using System.IO;
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
        SearchCommand = new RelayCommand(async SearchAsync);
        SaveMatchCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Match));
        SaveDuplicateCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Duplicate));
        SaveVariantCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Variant));
        SaveAnomalyCommand = new RelayCommand(async () => await SaveAsync(SourceKind.Anomaly));
        RebuildIndexCommand = new RelayCommand(async RebuildIndexAsync);
        NumistaSearchCommand = new RelayCommand(async NumistaSearchAsync);
        LoadObverseFileCommand = new RelayCommand(LoadObverseFile);
        LoadReverseFileCommand = new RelayCommand(LoadReverseFile);

        OpenCameraCommand = new RelayCommand(OpenCamera);
        StartBatchCommand = new RelayCommand(async StartBatchAsync);
        CaptureNextCommand = new RelayCommand(async () => await CaptureNextAsync());
        CancelBatchCommand = new RelayCommand(CancelBatch);
        SaveBatchCommand = new RelayCommand(async SaveBatchAsync);
        SaveAttributesCommand = new RelayCommand(async SaveAttributesAsync);
        LoadTypeCommand = new RelayCommand(async LoadTypeAsync);
        SaveTypeCommand = new RelayCommand(async SaveTypeAsync);
        RunAnomalyCommand = new RelayCommand(async RunAnomalyAsync);
        ValidateNumistaCommand = new RelayCommand(async ValidateNumistaAsync);
        ApplyNumistaItemCommand = new RelayCommand(() => ApplyNumistaItem());
        UndoAnnotationCommand = new RelayCommand(UndoAnnotation);
        ClearAnnotationsCommand = new RelayCommand(ClearAnnotations);
        SaveAnnotationsCommand = new RelayCommand(async () => await SaveAnnotationsAsync());

        _ = LoadReferencesAsync();
    }

    // ---- bindable properties ----
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Album { get => _album; set => SetProperty(ref _album, value); }
    public int PageNo { get => _pageNo; set => SetProperty(ref _pageNo, value); }
    public int OrderNo { get => _orderNo; set => SetProperty(ref _orderNo, value); }
    public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
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

    // ---- attribute editing properties ----
    public string MintYear { get => _mintYear; set => SetProperty(ref _mintYear, value); }
    public string Mint { get => _mint; set => SetProperty(ref _mint, value); }
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }
    public string AnomalyDesc { get => _anomalyDesc; set => SetProperty(ref _anomalyDesc, value); }
    public ObservableCollection<string> QualityItems { get; } = new();
    public ObservableCollection<string> OrientationItems { get; } = new();
    public TypeEditor TypeEditor { get; } = new();
    public bool TypeLoaded => TypeEditor.Id > 0;

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
    public RelayCommand SearchCommand { get; }
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

    public string T(string key) => _loc.T(key);

    // ==================== camera ====================
    private void OpenCamera()
    {
        if (_camera.IsOpen)
        {
            Status = _camera.Description ?? "camera open";
            return;
        }
        if (_camera.Open())
        {
            Status = $"{_camera.Description} - open";
        }
        else
        {
            Status = T("capture.camera_offline");
        }
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
                Status = $"crop: {resp.shape_detected}, {resp.orientation_applied_deg:F1}deg";
            }
            else
            {
                Status = resp?.error ?? T("status.error");
            }
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
    }

    private void LoadReverseFile()
    {
        var p = PickImage();
        if (p == null) return;
        _reversePath = p;
        ReverseImage = LoadImage(p);
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
                if (!string.IsNullOrEmpty(top.etalon_photo.obverse))
                    EtalonImage = LoadImage(top.etalon_photo.obverse);
                Status = $"top match: {top.coin_id} ({top.overall_pct:F1}%)";
            }
            else
            {
                Status = T("actions.not_determined");
            }
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

    // ==================== save ====================
    private async Task SaveAsync(SourceKind kind)
    {
        if (string.IsNullOrEmpty(_obversePath) && string.IsNullOrEmpty(_reversePath))
        {
            Status = T("status.error");
            return;
        }
        var id = _db.NextCoinId();
        string? ovPhoto = _obversePath, revPhoto = _reversePath, ovVec = null, revVec = null;
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
        string? path = _obversePath ?? _reversePath;
        if (path is null)
        {
            Status = T("status.error");
            return;
        }
        CoinSide side = _obversePath != null ? CoinSide.Obverse : CoinSide.Reverse;
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
                coin_id: _currentCoinId));
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
            HeatmapImage = File.Exists(resp.heatmap_path!) ? LoadImage(resp.heatmap_path) : null;
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
        string side = _obversePath != null ? "obverse" : "reverse";
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
        string side = _obversePath != null ? "obverse" : "reverse";
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
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        return bmp;
    }
}
