using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    public MainViewModel(AppSettings settings, BackendClient backend, DbService db,
        ICameraDevice camera, LocalizationService loc)
    {
        _settings = settings;
        _backend = backend;
        _db = db;
        _camera = camera;
        _loc = loc;

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
    }

    public string Status => _status;
    public string Album { get => _album; set => SetProperty(ref _album, value); }
    public int PageNo { get => _pageNo; set => SetProperty(ref _pageNo, value); }
    public int OrderNo { get => _orderNo; set => SetProperty(ref _orderNo, value); }
    public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
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

    public string T(string key) => _loc.T(key);

    // ---- capture ----
    private async Task ShootAsync(CoinSide side)
    {
        IsBusy = true;
        Status = _loc.T("status.backend_down");
        try
        {
            string? raw;
            if (_camera.TryGetFrame(out var frame) && frame != null)
            {
                raw = SaveFrame(frame, side);
            }
            else
            {
                Status = _loc.T("capture.camera_offline");
                IsBusy = false;
                return;
            }

            var req = new PreprocessRequest(
                raw_path: raw,
                side: side == CoinSide.Obverse ? "obverse" : "reverse",
                target_size: _settings.TargetSize,
                orientation_deg: Rotation);
            var resp = await _backend.PreprocessAsync(req);
            if (resp is { ok: true } && !string.IsNullOrEmpty(resp.crop_path))
            {
                if (side == CoinSide.Obverse) { _obversePath = resp.crop_path; ObverseImage = LoadImage(resp.crop_path); }
                else { _reversePath = resp.crop_path; ReverseImage = LoadImage(resp.crop_path); }
                Status = $"crop: {resp.shape_detected}, {resp.orientation_applied_deg:F1}deg";
            }
            else
            {
                Status = resp?.error ?? _loc.T("status.error");
            }
        }
        finally
        {
            IsBusy = false;
        }
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

    // ---- search ----
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(_obversePath) && string.IsNullOrEmpty(_reversePath))
        {
            Status = _loc.T("status.error");
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
                _currentCoinId = top.coin_id;
                if (!string.IsNullOrEmpty(top.etalon_photo.obverse))
                    EtalonImage = LoadImage(top.etalon_photo.obverse);
                Status = $"top match: {top.coin_id} ({top.overall_pct:F1}%)";
            }
            else
            {
                Status = _loc.T("actions.not_determined");
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
        Status = _loc.T("actions.swap_sides");
    }

    // ---- save ----
    private async Task SaveAsync(SourceKind kind)
    {
        var id = _db.NextCoinId();
        var coin = new Coin
        {
            Id = id,
            MintYear = null,
            Quality = null,
            Price = 0,
            AlbumName = Album,
            PageNo = PageNo,
            OrderNo = OrderNo,
            ObversePhoto = _obversePath,
            ReversePhoto = _reversePath,
            SourceKind = kind,
            IsAnomaly = kind == SourceKind.Anomaly,
            EtalonId = _currentCoinId
        };
        _db.SaveCoin(coin);
        Status = _loc.T("status.saved");
    }

    private async Task RebuildIndexAsync()
    {
        IsBusy = true;
        try
        {
            var counts = await _backend.RebuildIndexAsync();
            Status = counts is { }
                ? $"index: obverse={counts["obverse"]}, reverse={counts["reverse"]}"
                : _loc.T("status.error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- numista ----
    private async Task NumistaSearchAsync()
    {
        IsBusy = true;
        try
        {
            var resp = await _backend.NumistaSearchAsync(new NumistaSearchRequest(NumistaQuery, 20));
            Status = resp is { error: not null } ? resp.error : $"{resp?.items.Count ?? 0} items";
        }
        finally
        {
            IsBusy = false;
        }
    }

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
