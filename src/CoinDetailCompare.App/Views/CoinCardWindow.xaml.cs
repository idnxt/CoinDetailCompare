using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App.Views;

/// <summary>
/// Card of a saved coin: all layers as during photo (base image, heatmap,
/// etalon/current diff layer, manually rotatable/zoomable etalon photo,
/// annotations) plus editable catalog attributes.
/// </summary>
public partial class CoinCardWindow : Window
{
    private readonly CoinCardViewModel _vm;

    public CoinCardWindow(int coinId, AppSettings settings, BackendClient backend,
        DbService db, LocalizationService loc)
    {
        InitializeComponent();
        _vm = new CoinCardViewModel(coinId, settings, backend, db, loc);
        DataContext = _vm;
        Title = string.Format(loc.T("card.title"), coinId);
        UpdateSwatch(DiffColorBtn, _vm.DiffColor);
    }

    // ---- color picker (swatch button above the layer label) ----
    private void OnPickDiffColor(object sender, RoutedEventArgs e)
    {
        if (PickColor(_vm.DiffColor, out var c))
        {
            _vm.DiffColor = c;
            UpdateSwatch(DiffColorBtn, c);
        }
    }

    private bool PickColor(Color initial, out Color picked)
    {
        picked = initial;
        var dlg = new ColorPickerWindow(initial) { Owner = this };
        if (dlg.ShowDialog() != true)
            return false;
        picked = dlg.PickedColor;
        return true;
    }

    private static void UpdateSwatch(Button btn, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        btn.Background = b;
    }

    // ---- manual etalon photo: drag = rotate around center, wheel = zoom ----
    private double _dragStartAngle;
    private double _dragAngle0;

    private void OnEtalonPhotoMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image img || _vm.EtalonPhotoImage is null) return;
        var p = e.GetPosition(img);
        var center = new Point(img.ActualWidth / 2, img.ActualHeight / 2);
        _dragStartAngle = AngleDeg(p, center);
        _dragAngle0 = _vm.EtalonPhotoAngle;
        img.CaptureMouse();
    }

    private void OnEtalonPhotoMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Image img) return;
        var p = e.GetPosition(img);
        var center = new Point(img.ActualWidth / 2, img.ActualHeight / 2);
        _vm.EtalonPhotoAngle = Normalize(_dragAngle0 + (AngleDeg(p, center) - _dragStartAngle));
    }

    private void OnEtalonPhotoMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Image img)
            img.ReleaseMouseCapture();
    }

    private void OnEtalonPhotoWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.EtalonPhotoImage is null) return;
        // 80–120% range (base 100 ±20%): fine 2% steps so the wheel is usable.
        var factor = e.Delta > 0 ? 1.02 : 1.0 / 1.02;
        _vm.EtalonPhotoScale = Math.Clamp(_vm.EtalonPhotoScale * factor, 0.8, 1.2);
    }

    private static double AngleDeg(Point p, Point center)
        => Math.Atan2(p.Y - center.Y, p.X - center.X) * 180.0 / Math.PI;

    private static double Normalize(double a)
    {
        // Angle range is -180..180 (base 0).
        a %= 360;
        if (a > 180) a -= 360;
        else if (a <= -180) a += 360;
        return a;
    }
}
