using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App;

/// <summary>
/// 16:9 capture window: live viewfinder with a green coin template on the left,
/// obverse/reverse previews and shoot/close buttons on the right.
/// Hotkeys: 1 — shoot obverse, 2 — shoot reverse, Esc — close.
/// </summary>
public partial class CaptureWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;

    public CaptureWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Live preview pump (~25 fps).
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _timer.Tick += (_, _) => PumpFrame();
        Loaded += (_, _) => { if (_vm.IsCameraOpen) _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void PumpFrame()
    {
        if (!_vm.TryGetCameraFrame(out var frame) || frame is null)
            return;

        // The capture pipeline finds the coin at the *frame center*, so the
        // viewfinder must show exactly that center. The live stage in the
        // XAML is a square (1000x1000, rendered 1:1 via Stretch="Fill"), so
        // center-crop the frame to a square: displayed center == frame
        // center == template circle, no matter the frame or cell size.
        var fw = frame.PixelWidth;
        var fh = frame.PixelHeight;
        if (fw > 0 && fh > 0)
        {
            int side = Math.Min(fw, fh);
            LiveImage.Source = new CroppedBitmap(
                frame,
                new Int32Rect((fw - side) / 2, (fh - side) / 2, side, side));
            return;
        }

        LiveImage.Source = frame;
    }

    private void OnShootObverse(object sender, RoutedEventArgs e) => Shoot(_vm.ShootObverseCommand);

    private void OnShootReverse(object sender, RoutedEventArgs e) => Shoot(_vm.ShootReverseCommand);

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static void Shoot(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                Shoot(_vm.ShootObverseCommand);
                break;
            case Key.D2:
            case Key.NumPad2:
                Shoot(_vm.ShootReverseCommand);
                break;
            case Key.Escape:
                Close();
                break;
        }
    }
}
