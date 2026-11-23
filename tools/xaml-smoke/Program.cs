using System.IO;
using System.Windows;
using System.Windows.Media;
using CoinDetailCompare.App;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

// Headless smoke test: constructs CaptureWindow (and MainWindow) so any
// runtime XAML / TypeConverter failure surfaces before the user clicks.
internal static class Program
{
    [System.STAThread]
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
        var camera = new OpenCvCameraDevice(0);
        var loc = new LocalizationService(Path.Combine(baseDir, "Resources", "i18n"));
        var strings = new Strings(loc);
        app.Resources["Strings"] = strings;
        // Duplicate App.xaml resources (Card/Title/Btn/converters) in the test Application.
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

        var vm = new MainViewModel(settings, backend, db, camera, loc, strings);

        var capture = new CaptureWindow(vm);
        Console.WriteLine("CaptureWindow OK: " + capture.Width + "x" + capture.Height);

        var main = new MainWindow { DataContext = vm };
        Console.WriteLine("MainWindow OK");

        app.Shutdown();
        return 0;
    }
}
