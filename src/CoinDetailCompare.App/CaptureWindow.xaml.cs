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
