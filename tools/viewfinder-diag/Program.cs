using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoinDetailCompare.App;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

// Diagnostic: opens the real CaptureWindow, but feeds it SYNTHETIC frames
// (1920x1080, unique gradient + a red 40x40 marker at the exact frame center
// (960,540)). PumpFrame then runs exactly as in the real app. We render the
// window and compare:
//   - red marker center in the render  == where the frame center is shown
//   - green circle center in the render == where the template is
// If they differ, the template circle is mis-anchored (the reported bug).
internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.WriteLine("UNHANDLED: " + e.Exception);
            Environment.Exit(2);
        };

        var baseDir = @"E:\CoinDetailCompare\src\CoinDetailCompare.App\bin\Release\net8.0-windows\win-x64";
        var settings = new SettingsService(baseDir).Current;
        var backend = new BackendClient(settings.BackendBaseUrl);
        var db = new DbService(settings.DbPath, settings.SchemaPath);
        var loc = new LocalizationService(Path.Combine(baseDir, "Resources", "i18n"));
        var strings = new Strings(loc);
        app.Resources["Strings"] = strings;
        app.Resources["InverseBool"] = new CoinDetailCompare.App.Converters.InverseBoolConverter();
        app.Resources["Pct"] = new CoinDetailCompare.App.Converters.PercentConverter();
        app.Resources["BoolToVis"] = new CoinDetailCompare.App.Converters.BoolToVisibilityConverter();

        var card = new Style(typeof(System.Windows.Controls.Border));
        card.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8))));
        card.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD))));
        card.Setters.Add(new Setter(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1)));
        card.Setters.Add(new Setter(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6)));
        card.Setters.Add(new Setter(System.Windows.Controls.Border.PaddingProperty, new Thickness(12)));
        app.Resources["Card"] = card;

        var title = new Style(typeof(System.Windows.Controls.TextBlock));
        title.Setters.Add(new Setter(System.Windows.Controls.TextBlock.FontSizeProperty, 15.0));
        title.Setters.Add(new Setter(System.Windows.Controls.TextBlock.FontWeightProperty, FontWeights.SemiBold));
        title.Setters.Add(new Setter(System.Windows.Controls.TextBlock.MarginProperty, new Thickness(0, 0, 0, 8)));
        app.Resources["Title"] = title;

        var btn = new Style(typeof(System.Windows.Controls.Button));
        btn.Setters.Add(new Setter(System.Windows.Controls.Button.PaddingProperty, new Thickness(10, 6, 10, 6)));
        btn.Setters.Add(new Setter(System.Windows.Controls.Button.MarginProperty, new Thickness(0, 0, 0, 6)));
        btn.Setters.Add(new Setter(System.Windows.Controls.Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        app.Resources["Btn"] = btn;

        var camera = new FakeCamera();
        var vm = new MainViewModel(settings, backend, db, camera, loc, strings);
        var dummyMain = new MainWindow { DataContext = vm, Left = -4000, Top = -4000 };
        dummyMain.WindowStartupLocation = WindowStartupLocation.Manual;
        app.MainWindow = dummyMain;
        dummyMain.Show();
        vm.OpenCameraCommand.Execute(null);
        if (!vm.IsCameraOpen)
        {
            Console.WriteLine("CAMERA NOT OPEN");
            return 1;
        }

        CaptureWindow? win = null;
        for (int i = 0; i < app.Windows.Count; i++)
            win ??= app.Windows[i] as CaptureWindow;
        if (win is null)
        {
            Console.WriteLine("NO CAPTURE WINDOW");
            return 1;
        }
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        win.Left = 100;
        win.Top = 100;
        win.Show();

        var done = new System.Threading.ManualResetEventSlim(false);
        app.Dispatcher.BeginInvoke(() =>
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            t.Tick += (_, _) =>
            {
                try { MeasureAndSave(win!); }
                catch (System.Exception ex) { Console.WriteLine("DIAG FAIL: " + ex); }
                finally { done.Set(); app.Shutdown(); }
            };
            t.Start();
        });
        app.Run();
        done.Wait(15000);
        return 0;
    }

    private static void MeasureAndSave(CaptureWindow win)
    {
        Console.WriteLine($"window: {win.ActualWidth:F1} x {win.ActualHeight:F1}");
        var shot = new RenderTargetBitmap((int)win.ActualWidth, (int)win.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        shot.Render(win);

        var dir = @"C:\Users\admin\AppData\Local\Temp\opencode\viewfinder";
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "diag_window.png");
        Save(shot, path);
        Console.WriteLine("saved " + path);

        // Find the red marker (frame center) and the green circle stroke.
        int w = shot.PixelWidth, h = shot.PixelHeight;
        var px = new byte[w * h * 4];
        shot.CopyPixels(px, w * 4, 0);
        double rx = 0, ry = 0; int rn = 0;
        double gx = 0, gy = 0; int gn = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int b = px[i], g = px[i + 1], r = px[i + 2];
                // red marker (possibly under the 20% translucent green fill)
                if (r > 150 && g < 80 && b < 80) { rx += x; ry += y; rn++; }
                // green circle stroke #00E676
                if (g > 200 && r < 30 && b > 90 && b < 150) { gx += x; gy += y; gn++; }
            }
        }
        Console.WriteLine($"red marker (frame center) : n={rn} center=({(rn > 0 ? rx / rn : 0):F1}, {(rn > 0 ? ry / rn : 0):F1})");
        Console.WriteLine($"green circle center         : n={gn} center=({(gn > 0 ? gx / gn : 0):F1}, {(gn > 0 ? gy / gn : 0):F1})");
        if (rn > 0 && gn > 0)
            Console.WriteLine($"DELTA circle - frameCenter = ({(gx / gn - rx / rn):F1}, {(gy / gn - ry / rn):F1})  (negative x = circle LEFT of crop center)");
    }

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// 1920x1080 BGRA frame: unique per-pixel gradient (r from x, g from y)
    /// plus a pure-red 40x40 marker centered exactly at (960, 540).
    /// </summary>
    private sealed class FakeCamera : ICameraDevice
    {
        public string? Description => "FakeCamera";
        public bool IsOpen { get; private set; }
        public bool Open() { IsOpen = true; return true; }

        public bool TryGetFrame(out BitmapSource? frame)
        {
            frame = null;
            if (!IsOpen) return false;
            int w = 1920, h = 1080;
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = 128;     // b
                px[i + 1] = 0;   // g
                px[i + 2] = 0;   // r
                px[i + 3] = 255;
            }
            // red marker 40x40 centered at (960, 540)
            for (int y = 540 - 20; y < 540 + 20; y++)
            {
                for (int x = 960 - 20; x < 960 + 20; x++)
                {
                    int i = (y * w + x) * 4;
                    px[i] = 0; px[i + 1] = 0; px[i + 2] = 255; px[i + 3] = 255;
                }
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            frame = bmp;
            return true;
        }

        private static double x255(int x, int w) => (x * 255) / (w - 1);
        private static double y255(int y, int h) => (y * 255) / (h - 1);

        public void Close() => IsOpen = false;
        public void Dispose() { }
    }
}
