using System.IO;
using System.Windows;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var settingsSvc = new SettingsService(baseDir);
        var settings = settingsSvc.Current;

        var backend = new BackendClient(settings.BackendBaseUrl);
        var db = new DbService(settings.DbPath, settings.SchemaPath);
        int camIndex = OpenCvCameraDevice.DetectFirstCamera();
        var camera = camIndex >= 0 ? new OpenCvCameraDevice(camIndex) : new OpenCvCameraDevice(0);
        var loc = new LocalizationService(Path.Combine(baseDir, "Resources", "i18n"));
        loc.SetLanguage(settings.UiLanguage);

        // XAML binds labels via {Binding ['key'], Source={StaticResource Strings}}.
        var strings = new Strings(loc);
        Resources["Strings"] = strings;

        var vm = new MainViewModel(settings, backend, db, camera, loc, strings);
        var window = new MainWindow { DataContext = vm };
        window.Title = strings["app.title"];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Language) && MainWindow != null)
                MainWindow.Title = strings["app.title"];
        };
        MainWindow = window;
        window.Show();
    }
}
