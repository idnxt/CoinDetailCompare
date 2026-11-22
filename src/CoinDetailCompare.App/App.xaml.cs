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
        var camera = new CameraDeviceStub();
        var loc = new LocalizationService(Path.Combine(baseDir, "Resources", "i18n"));
        loc.SetLanguage(settings.UiLanguage);

        var vm = new MainViewModel(settings, backend, db, camera, loc);
        var window = new MainWindow { DataContext = vm };
        window.Title = loc.T("app.title");
        MainWindow = window;
        window.Show();
    }
}
