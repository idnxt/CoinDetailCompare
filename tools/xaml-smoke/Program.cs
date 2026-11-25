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
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Console.WriteLine("UNOBSERVED: " + e.Exception);
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
        app.Resources["Opacity01"] = new CoinDetailCompare.App.Converters.OpacityConverter();

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

        var coinCard = new CoinDetailCompare.App.Views.CoinCardWindow(4, settings, backend, db, loc);
        Console.WriteLine("CoinCardWindow OK");

        // The VM kicks off fire-and-forget async work from its constructor:
        // let the real dispatcher pump it, then shut down.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        timer.Tick += (s, e) =>
        {
            Console.WriteLine("Card settled.");
            try
            {
                Console.WriteLine("step: show card");
                coinCard.Show();
                coinCard.UpdateLayout();
                Console.WriteLine("step: scale/angle slider test");
                var overlay = (System.Windows.Controls.Image)coinCard.FindName("EtalonPhotoOverlay");
                var tg = (System.Windows.Media.TransformGroup)overlay.RenderTransform;
                var st = (System.Windows.Media.ScaleTransform)tg.Children[0];
                var rt = (System.Windows.Media.RotateTransform)tg.Children[1];
                var scaleSlider = (System.Windows.Controls.Slider)coinCard.FindName("EtalonPhotoScaleSlider");
                var angleSlider = (System.Windows.Controls.Slider)coinCard.FindName("EtalonPhotoAngleSlider");
                double s0 = scaleSlider.Value, a0 = angleSlider.Value;
                Console.WriteLine($"before: slider=({s0}, {a0}) transform=({st.ScaleX}, {rt.Angle})");
                scaleSlider.Value = s0 + 10; // 100 -> 110 (range 80..120)
                angleSlider.Value = a0 - 10; // 0 -> -10 (range -180..180)
                coinCard.UpdateLayout();
                var vm = coinCard.DataContext as CoinDetailCompare.App.ViewModels.CoinCardViewModel;
                Console.WriteLine($"after sliders: slider=({scaleSlider.Value}, {angleSlider.Value}) vm=({vm.EtalonPhotoScale}, {vm.EtalonPhotoAngle}) transform=({st.ScaleX}, {rt.Angle})");
                // NumericUpDown: type into the scale box, then move focus away (commit on LostFocus)
                var scaleBox = (CoinDetailCompare.App.Controls.NumericUpDown)coinCard.FindName("ScaleBox");
                var boxText = (System.Windows.Controls.TextBox)scaleBox.FindName("TextBox");
                boxText.Text = "101.50";
                scaleBox.CommitEdit(); // same path as Enter / LostFocus
                coinCard.UpdateLayout();
                Console.WriteLine($"after box edit: vm=({vm.EtalonPhotoScale}, {vm.EtalonPhotoAngle}) transform=({st.ScaleX}, {rt.Angle}) box={boxText.Text}");
                // spinner arrow: +1 step (0.01) on the angle box
                var angleBox = (CoinDetailCompare.App.Controls.NumericUpDown)coinCard.FindName("AngleBox");
                var up = (System.Windows.Controls.Primitives.RepeatButton)angleBox.FindName("UpButton");
                up.RaiseEvent(new System.Windows.RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent, up));
                coinCard.UpdateLayout();
                var angleBoxText = (System.Windows.Controls.TextBox)angleBox.FindName("TextBox");
                Console.WriteLine($"after arrow: vm=({vm.EtalonPhotoScale}, {vm.EtalonPhotoAngle}) transform=({st.ScaleX}, {rt.Angle}) angleBox={angleBoxText.Text}");
                // the diff layer must follow the etalon photo's rotation/zoom
                var diffOverlay = (System.Windows.Controls.Image)coinCard.FindName("DiffOverlay");
                var dtg = (System.Windows.Media.TransformGroup)diffOverlay.RenderTransform;
                var dst = (System.Windows.Media.ScaleTransform)dtg.Children[0];
                var drot = (System.Windows.Media.RotateTransform)dtg.Children[1];
                Console.WriteLine($"diff overlay: loaded={diffOverlay.Source != null} transform=({dst.ScaleX}, {drot.Angle}) etalon=({st.ScaleX}, {rt.Angle}) inSync={(dst.ScaleX == st.ScaleX && drot.Angle == rt.Angle)}");
                Console.WriteLine($"vm.DiffImage={(vm.DiffImage != null)} status={vm.Status}");
                Console.WriteLine("step: find similar + pick etalon");
                int origEtalon = vm.EtalonId ?? -1;
                vm.FindSimilarCommand.Execute(null);
                var frame = new System.Windows.Threading.DispatcherFrame();
                var waitT = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                waitT.Tick += (s, e) =>
                {
                    if (!vm.IsBusy) { waitT.Stop(); frame.Continue = false; }
                };
                waitT.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Console.WriteLine($"similar: count={vm.SimilarCandidates.Count} status={vm.Status}");
                if (vm.SimilarCandidates.Count > 0)
                {
                    foreach (var cand in vm.SimilarCandidates) Console.WriteLine("  cand: " + cand);
                    var orig = vm.SimilarCandidates.FirstOrDefault(x => x.CoinId == origEtalon);
                    if (orig is not null)
                    {
                        vm.SelectedSimilar = vm.SimilarCandidates[0];
                        Console.WriteLine($"canPick={(vm.CanPickEtalon)}");
                        vm.PickEtalonCommand.Execute(null);
                        Console.WriteLine($"picked: etalon={vm.EtalonId} label={vm.EtalonLabel} status={vm.Status}");
                        // restore the original etalon so the catalog data is untouched
                        vm.SelectedSimilar = orig;
                        vm.PickEtalonCommand.Execute(null);
                        Console.WriteLine($"restored: etalon={vm.EtalonId} (orig {origEtalon}) ok={vm.EtalonId == origEtalon}");
                    }
                    else
                    {
                        Console.WriteLine($"orig etalon {origEtalon} not in candidate list - pick test skipped");
                    }
                }
                Console.WriteLine("step: create picker");
                var picker = new CoinDetailCompare.App.Views.ColorPickerWindow(
                    System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF)) { Owner = coinCard };
                Console.WriteLine("step: show dialog");
                var t2 = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                t2.Tick += (s2, e2) =>
                {
                    try
                    {
                        // click the 2nd preset swatch (red), then OK
                        var grid = (System.Windows.Controls.Primitives.UniformGrid)picker.FindName("SwatchGrid");
                        var swatch = (System.Windows.Controls.Button)grid.Children[1];
                        swatch.RaiseEvent(new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives.ButtonBase.ClickEvent, swatch));
                        var ok = (System.Windows.Controls.Button)picker.FindName("OkButton");
                        ok.RaiseEvent(new System.Windows.RoutedEventArgs(
                            System.Windows.Controls.Primitives.ButtonBase.ClickEvent, ok));
                    }
                    catch (System.Exception ex) { Console.WriteLine("PICK ERROR: " + ex); }
                };
                t2.Start();
                var result = picker.ShowDialog();
                Console.WriteLine("Picker result=" + result + " color=" + picker.PickedColor);
            }
            catch (System.Exception ex) { Console.WriteLine("CARD PICK ERROR: " + ex); }
            app.Shutdown();
        };
        timer.Start();
        app.Run();
        return 0;
    }
}
