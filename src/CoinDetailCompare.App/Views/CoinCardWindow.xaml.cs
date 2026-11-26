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

    // ---- manual etalon photo: drag = shift along X/Y (±100 px), wheel = zoom ----
    private Point _dragStartPos;
    private double _dragOffsetX0;
    private double _dragOffsetY0;

    private void OnEtalonPhotoMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image img || _vm.EtalonPhotoImage is null) return;
        // Measure in the WINDOW's coordinate space, not the image's own:
        // the image's local space is shifted by its own TranslateTransform,
        // so GetPosition(img) feeds the offset back into the next move
        // event and the photo jitters (and chases the mouse at 0.5x).
        _dragStartPos = e.GetPosition(this);
        _dragOffsetX0 = _vm.EtalonPhotoOffsetX;
        _dragOffsetY0 = _vm.EtalonPhotoOffsetY;
        img.CaptureMouse();
        Keyboard.Focus(img); // arrows then move the photo by 1 px
    }

    private void OnEtalonPhotoMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Image img) return;
        var p = e.GetPosition(this);
        // The VM clamps both axes to -100..100 px (base 0,0).
        _vm.EtalonPhotoOffsetX = _dragOffsetX0 + (p.X - _dragStartPos.X);
        _vm.EtalonPhotoOffsetY = _dragOffsetY0 + (p.Y - _dragStartPos.Y);
    }

    private void OnEtalonPhotoMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Image img)
            img.ReleaseMouseCapture();
    }

    // ---- arrow keys: 1 px nudge once the photo is "active" (clicked) ----
    private void OnEtalonPhotoKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:  _vm.NudgeEtalonPhoto(-1, 0); e.Handled = true; break;
            case Key.Right: _vm.NudgeEtalonPhoto(1, 0);  e.Handled = true; break;
            case Key.Up:    _vm.NudgeEtalonPhoto(0, -1); e.Handled = true; break;
            case Key.Down:  _vm.NudgeEtalonPhoto(0, 1);  e.Handled = true; break;
            default: return;
        }
    }

    private void OnEtalonPhotoWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.EtalonPhotoImage is null) return;
        // 80–120% range (base 100 ±20%): fine 2% steps so the wheel is usable.
        var factor = e.Delta > 0 ? 1.02 : 1.0 / 1.02;
        _vm.EtalonPhotoScale = Math.Clamp(_vm.EtalonPhotoScale * factor, 0.8, 1.2);
    }
}
