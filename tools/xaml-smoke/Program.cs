using System.IO;
using System.Windows;
using System.Windows.Media;
using CoinDetailCompare.App;
using CoinDetailCompare.App.Models;
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
        app.MainWindow = main; // App.xaml StartupUri equivalent; without it the first
        // constructed window (the unshown CaptureWindow) becomes MainWindow
        Console.WriteLine("MainWindow OK");
        var mvm = vm; // the card's VM shadows `vm` later in the test

        var coinCard = new CoinDetailCompare.App.Views.CoinCardWindow(4, settings, backend, db, loc);
        Console.WriteLine("CoinCardWindow OK");

        // The VM kicks off fire-and-forget async work from its constructor:
        // let the real dispatcher pump it, then shut down.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        timer.Tick += async (s, e) =>
        {
            // The handler is async: while it awaits, a later 15 s tick would
            // start the whole suite in parallel (and later collide with
            // app.Shutdown() on a window Show). Stop the timer up front so the
            // suite runs exactly once.
            timer.Stop();
            Console.WriteLine("Card settled.");
            try
            {
                Console.WriteLine("step: catalog tree");
                main.Show();
                main.UpdateLayout();
                var roots = mvm.CatalogTreeRoots;
                var firstAlbum = roots.FirstOrDefault(a => !a.IsLocked);
                var countries = roots.FirstOrDefault(a => a.IsLocked);
                var firstCoin = firstAlbum?.Children.FirstOrDefault()?.Children.FirstOrDefault()?.Children.FirstOrDefault();
                Console.WriteLine($"catalog tree: rootTitles=[{string.Join(" | ", roots.Select(r => r.Title + (r.IsLocked ? " (locked)" : "")))}] albums={firstAlbum?.Children.Count} firstAlbumPages={firstAlbum?.Children.FirstOrDefault()?.Children.Count} firstPageCoins={firstAlbum?.Children.FirstOrDefault()?.Children.FirstOrDefault()?.Children.Count} firstCoin='{firstCoin?.Title}' coinId={firstCoin?.CoinId} countriesLocked={countries?.IsLocked}");
                var tree = (System.Windows.Controls.TreeView)main.FindName("CatalogTree");
                tree.UpdateLayout();
                Console.WriteLine($"catalog treeview: items={tree.Items.Count}");
                // headless: child containers are generated lazily — pump the
                // dispatcher between each expansion pass
                void PumpFrames()
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var p = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(150)
                    };
                    p.Tick += (_, _) => { p.Stop(); frame.Continue = false; };
                    p.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                // tree layout: "Альбомы" (root) -> album -> page -> coin
                var rootItem = (System.Windows.Controls.TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                rootItem.IsExpanded = true;
                rootItem.UpdateLayout();
                PumpFrames();
                main.UpdateLayout();
                var albumItem = (System.Windows.Controls.TreeViewItem)rootItem.ItemContainerGenerator.ContainerFromIndex(0);
                albumItem.IsExpanded = true;
                albumItem.UpdateLayout();
                PumpFrames();
                main.UpdateLayout();
                var pageItem = (System.Windows.Controls.TreeViewItem)albumItem.ItemContainerGenerator.ContainerFromIndex(0);
                pageItem.IsExpanded = true;
                pageItem.UpdateLayout();
                PumpFrames();
                main.UpdateLayout();
                PumpFrames();
                var coinItem = (System.Windows.Controls.TreeViewItem)pageItem.ItemContainerGenerator.ContainerFromIndex(0);
                // Double-click / Enter / right-click on a coin row are wired on the
                // TreeView itself via tunneling Preview* handlers; the node is
                // resolved from the event's OriginalSource (the row's TextBlock)
                // through the visual tree. (The old per-item EventSetter resolved
                // to UIElement.MouseLeftButtonDownEvent, a Direct-strategy legacy
                // event that never fired for clicks on the row's TextBlock.)
                int coinId = firstCoin?.CoinId ?? 0;
                for (int t = 0; coinItem is null && t < 8; t++)
                {
                    PumpFrames();
                    pageItem.UpdateLayout();
                    coinItem = (System.Windows.Controls.TreeViewItem)pageItem.ItemContainerGenerator.ContainerFromIndex(0);
                }
                var row = coinItem ?? FindCoinRow(main, coinId);
                Check(row is not null, "coin row container is realized in the tree");
                var rowText = FindTextBlock(row!);
                Check(rowText is not null, "coin row's TextBlock found");
                Check(row!.DataContext is CatalogTreeNode rowNode && rowNode.CoinId == coinId,
                    "row's DataContext is the coin node");
                // Neither mouse nor key events can be synthesized through the
                // public API (MouseDevice/KeyboardDevice ctors are internal,
                // ClickCount's setter is internal), so uninitialized event args
                // are created and their fields (_count, _key, _originalSource)
                // set via reflection — the handlers only read those.
                System.Windows.Input.MouseButtonEventArgs MouseArgs(DependencyObject src, int clicks, RoutedEvent ev)
                {
                    var a = (System.Windows.Input.MouseButtonEventArgs)
                        System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                            typeof(System.Windows.Input.MouseButtonEventArgs));
                    SetWpfField(a, "_count", clicks);
                    SetWpfField(a, "_routedEvent", ev); // required by e.Handled = true
                    SetWpfField(a, "_originalSource", src);
                    return a;
                }
                void RunTreeHandler(string name, RoutedEventArgs args)
                {
                    var m = typeof(MainWindow).GetMethod(name,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new Exception("handler not found: " + name);
                    m.Invoke(main, new object[] { main, args });
                }
                // EditCoinFromTree opens the card with ShowDialog (modal); a timer
                // closes it so the headless test can continue
                var closer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                closer.Tick += (s, e) =>
                {
                    foreach (var w in Application.Current.Windows)
                    {
                        if (w is System.Windows.Window win
                            && win.GetType() == typeof(CoinDetailCompare.App.Views.CoinCardWindow)
                            && win != coinCard && win.IsVisible)
                            win.Close();
                    }
                };
                // 1) double-click on the row's TextBlock -> the card opens
                closer.Start();
                RunTreeHandler("CatalogTree_PreviewMouseLeftButtonDown",
                    MouseArgs(rowText, 2, System.Windows.UIElement.PreviewMouseLeftButtonDownEvent));
                closer.Stop();
                Console.WriteLine($"tree double-click (handler): card opened and closed; roots={mvm.CatalogTreeRoots.Count}");
                // 2) Enter on the focused row (OriginalSource = the row itself)
                closer.Start();
                var enter = (System.Windows.Input.KeyEventArgs)
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                        typeof(System.Windows.Input.KeyEventArgs));
                SetWpfField(enter, "_key", System.Windows.Input.Key.Enter);
                SetWpfField(enter, "_routedEvent", System.Windows.UIElement.PreviewKeyDownEvent);
                SetWpfField(enter, "_originalSource", row!);
                RunTreeHandler("CatalogTree_PreviewKeyDown", enter);
                closer.Stop();
                Console.WriteLine("tree Enter (handler): card opened and closed");
                // 3) right-click on the row -> 2-item context menu (Open / Delete).
                // Closing the card triggers a fire-and-forget tree refresh: the
                // tree is rebuilt with fresh nodes and collapsed containers, so
                // re-expand root -> album -> page and re-find the coin row.
                PumpFrames();
                PumpFrames();
                var rootItem2 = (System.Windows.Controls.TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                rootItem2.IsExpanded = true;
                rootItem2.UpdateLayout();
                PumpFrames();
                var albumItem2 = (System.Windows.Controls.TreeViewItem)rootItem2.ItemContainerGenerator.ContainerFromIndex(0);
                albumItem2.IsExpanded = true;
                albumItem2.UpdateLayout();
                PumpFrames();
                var pageItem2 = (System.Windows.Controls.TreeViewItem)albumItem2.ItemContainerGenerator.ContainerFromIndex(0);
                pageItem2.IsExpanded = true;
                pageItem2.UpdateLayout();
                PumpFrames();
                main.UpdateLayout();
                row = null;
                for (int t = 0; row is null && t < 8; t++)
                {
                    PumpFrames();
                    row = (System.Windows.Controls.TreeViewItem)pageItem2.ItemContainerGenerator.ContainerFromIndex(0)
                          ?? FindCoinRow(main, coinId);
                }
                Check(row is not null, "coin row present after refresh");
                Check(row!.DataContext is CatalogTreeNode rowNode2 && rowNode2.CoinId == coinId,
                    "refreshed row's DataContext is the coin node");
                rowText = FindTextBlock(row!);
                Check(rowText is not null, "refreshed row's TextBlock found");
                RunTreeHandler("CatalogTree_PreviewMouseRightButtonDown",
                    MouseArgs(rowText, 1, System.Windows.UIElement.PreviewMouseRightButtonDownEvent));
                var ctx = row!.ContextMenu;
                Check(ctx is not null && ctx.Items.Count == 2, "right-click attached a 2-item context menu");
                var itemOpen = (System.Windows.Controls.MenuItem)ctx!.Items[0];
                var itemDel = (System.Windows.Controls.MenuItem)ctx.Items[1];
                Check(itemOpen.Command == mvm.EditCoinFromTreeCommand, "menu item 1: open -> EditCoinFromTreeCommand");
                Check(itemDel.Command == mvm.DeleteCoinFromTreeCommand, "menu item 2: delete -> DeleteCoinFromTreeCommand");
                Check(itemDel.CommandParameter is CatalogTreeNode delNode && delNode.CoinId == coinId,
                    "menu delete item carries the coin node");
                row.ContextMenu = null;
                // a refresh must keep the branches the user had opened
                // (albums -> album -> page were expanded above)
                mvm.RefreshCatalogTree();
                PumpFrames();
                var rootItem3 = (System.Windows.Controls.TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                rootItem3.UpdateLayout();
                PumpFrames();
                var albumItem3 = (System.Windows.Controls.TreeViewItem)rootItem3.ItemContainerGenerator.ContainerFromIndex(0);
                albumItem3.UpdateLayout();
                PumpFrames();
                var pageItem3 = (System.Windows.Controls.TreeViewItem)albumItem3.ItemContainerGenerator.ContainerFromIndex(0);
                pageItem3.UpdateLayout();
                PumpFrames();
                Check(rootItem3.IsExpanded && albumItem3.IsExpanded && pageItem3.IsExpanded,
                    "refresh kept all opened branches expanded (albums -> album -> page)");
                Check(pageItem3.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.Controls.TreeViewItem,
                    "refreshed page item still has its coin rows");
                Console.WriteLine("tree refresh: opened branches preserved");
                // the old left-block buttons are gone; the same functionality is
                // reachable via the right-click menu on "Open capture window"
                var camBtn = FindButtonWithContextMenu(main);
                Console.WriteLine($"capture ctxmenu: found={camBtn != null} items={camBtn?.ContextMenu.Items.Count}");
                Console.WriteLine($"load cmds alive: obverse={mvm.LoadObverseFileCommand.CanExecute(null)} reverse={mvm.LoadReverseFileCommand.CanExecute(null)} swap={mvm.SwapSidesCommand.CanExecute(null)}");
                // DELETE /coins/{id} end-to-end: a freshly created coin row plus
                // its files are removed through the backend endpoint (DB row,
                // photo/vector/heatmap files, FAISS entries)
                var delId = db.NextCoinId();
                db.SaveCoin(new Coin
                {
                    Id = delId,
                    MintYear = "2027",
                    AlbumName = "SmokeDel",
                    PageNo = 1,
                    OrderNo = 77,
                });
                File.WriteAllBytes($@"E:\CoinDetailCompare\coimg\{delId}_obverse.png",
                    new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                File.WriteAllBytes($@"E:\CoinDetailCompare\coimg\{delId}_obverse.npy",
                    new byte[] { 0x94, 0x2D, 0x4E, 0x55, 0x4D, 0x50, 0x59 });
                File.WriteAllBytes($@"E:\CoinDetailCompare\coimg\{delId}_obverse_heatmap.png",
                    new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                var delHttp = new System.Net.Http.HttpClient { BaseAddress = new Uri(settings.BackendBaseUrl) };
                var delResp = await delHttp.DeleteAsync($"/coins/{delId}");
                Check(delResp.StatusCode == System.Net.HttpStatusCode.OK,
                    $"DELETE /coins/{delId} -> 200 (got {(int)delResp.StatusCode} {delResp.ReasonPhrase})");
                Check(db.GetCoin(delId) is null, "deleted coin row is gone from the DB");
                Check(!File.Exists($@"E:\CoinDetailCompare\coimg\{delId}_obverse.png"), "deleted coin's photo removed");
                Check(!File.Exists($@"E:\CoinDetailCompare\coimg\{delId}_obverse.npy"), "deleted coin's vector removed");
                Check(!File.Exists($@"E:\CoinDetailCompare\coimg\{delId}_obverse_heatmap.png"), "deleted coin's heatmap removed");
                var delAgain = await delHttp.DeleteAsync($"/coins/{delId}");
                Check(delAgain.StatusCode == System.Net.HttpStatusCode.NotFound, "second DELETE -> 404");
                Console.WriteLine($"tree delete: coin {delId} removed (row, files, vectors)");
                Console.WriteLine("step: show card");
                coinCard.Show();
                coinCard.UpdateLayout();
                Console.WriteLine($"diag card: IsVisible={coinCard.IsVisible}");
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
                // XY pan: slider + 0.01-precision box drive the TranslateTransform
                // (mouse-drag rotation was replaced by XY shifting, base 0, ±100 px)
                var xSlider = (System.Windows.Controls.Slider)coinCard.FindName("EtalonPhotoXSlider");
                var ySlider = (System.Windows.Controls.Slider)coinCard.FindName("EtalonPhotoYSlider");
                var trans = (System.Windows.Media.TranslateTransform)tg.Children[2];
                double x0 = xSlider.Value, y0 = ySlider.Value;
                xSlider.Value = x0 + 25; // e.g. 0 -> 25 (range -100..100)
                ySlider.Value = y0 - 15; // e.g. 0 -> -15
                coinCard.UpdateLayout();
                var xBox = (CoinDetailCompare.App.Controls.NumericUpDown)coinCard.FindName("XBox");
                var xBoxText = (System.Windows.Controls.TextBox)xBox.FindName("TextBox");
                xBoxText.Text = "30.00";
                xBox.CommitEdit(); // same path as Enter / LostFocus
                coinCard.UpdateLayout();
                Console.WriteLine($"after XY: slider=({xSlider.Value}, {ySlider.Value}) vm=({vm.EtalonPhotoOffsetX}, {vm.EtalonPhotoOffsetY}) translate=({trans.X}, {trans.Y}) xBox={xBoxText.Text}");
                // the diff layer must follow the etalon photo's zoom/rotation/shift
                var diffOverlay = (System.Windows.Controls.Image)coinCard.FindName("DiffOverlay");
                var dtg = (System.Windows.Media.TransformGroup)diffOverlay.RenderTransform;
                var dst = (System.Windows.Media.ScaleTransform)dtg.Children[0];
                var drot = (System.Windows.Media.RotateTransform)dtg.Children[1];
                var dtrans = (System.Windows.Media.TranslateTransform)dtg.Children[2];
                Console.WriteLine($"diff overlay: loaded={diffOverlay.Source != null} translate=({dtrans.X}, {dtrans.Y}) etalon=({trans.X}, {trans.Y}) inSync={(dst.ScaleX == st.ScaleX && drot.Angle == rt.Angle && dtrans.X == trans.X && dtrans.Y == trans.Y)}");
                Console.WriteLine($"vm.DiffImage={(vm.DiffImage != null)} status={vm.Status}");
                // keyboard nudging: the photo is "active" after a click (focus),
                // then the arrow keys move it by 1 px (VM path, 1:1 with the handler)
                double kx = vm.EtalonPhotoOffsetX, ky = vm.EtalonPhotoOffsetY;
                vm.NudgeEtalonPhoto(-1, +1);
                coinCard.UpdateLayout();
                Console.WriteLine($"after keyboard: vm=({vm.EtalonPhotoOffsetX}, {vm.EtalonPhotoOffsetY}) expect=({kx - 1}, {ky + 1}) ok={(vm.EtalonPhotoOffsetX == kx - 1 && vm.EtalonPhotoOffsetY == ky + 1)}");
                Console.WriteLine("step: find similar + preview + pick etalon");
                void WaitIdle()
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var waitT = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(200)
                    };
                    waitT.Tick += (s, e) => { if (!vm.IsBusy) { waitT.Stop(); frame.Continue = false; } };
                    waitT.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                void WaitDiff(bool expect)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var waitT = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(200)
                    };
                    waitT.Tick += (s, e) =>
                    {
                        if ((vm.DiffImage != null) == expect || sw.Elapsed > TimeSpan.FromSeconds(60))
                        { waitT.Stop(); frame.Continue = false; }
                    };
                    waitT.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                int origEtalon = vm.EtalonId ?? -1;
                // pick now also copies the etalon's fields into the duplicate,
                // so snapshot the coin and restore it verbatim afterwards
                var snapshot = db.GetCoin(vm.CoinId);
                // Wait on the concrete observable (candidates populated), not IsBusy:
                // the fire-and-forget command makes IsBusy unreliable here.
                void WaitCandidates()
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var waitT = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(200) };
                    waitT.Tick += (s, e) =>
                    {
                        if (vm.SimilarCandidates.Count > 0 || sw.Elapsed > TimeSpan.FromSeconds(60)) { waitT.Stop(); frame.Continue = false; }
                    };
                    waitT.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                Console.WriteLine($"before find-similar: IsBusy={vm.IsBusy} canExec={vm.FindSimilarCommand.CanExecute(null)} status={vm.Status}");
                vm.FindSimilarCommand.Execute(null);
                Console.WriteLine($"just after execute: IsBusy={vm.IsBusy}");
                WaitCandidates();
                Console.WriteLine($"similar: count={vm.SimilarCandidates.Count} IsBusy={vm.IsBusy} status={vm.Status}");
                if (vm.SimilarCandidates.Count > 0)
                {
                    foreach (var cand in vm.SimilarCandidates) Console.WriteLine("  cand: " + cand);
                    var orig = vm.SimilarCandidates.FirstOrDefault(x => x.CoinId == origEtalon);
                    if (orig is not null)
                    {
                        // selecting a candidate previews it in the etalon layers; the DB must not change
                        vm.SelectedSimilar = vm.SimilarCandidates[0];
                        WaitIdle();
                        WaitDiff(true);
                        Console.WriteLine($"preview: db={vm.EtalonId} (orig {origEtalon}) previewId={vm.PreviewEtalonId} photo={(vm.EtalonPhotoImage != null)} diff={(vm.DiffImage != null)} status={vm.Status}");
                        // cancel the preview -> back to the saved etalon, DB untouched
                        vm.ResetEtalonCommand.Execute(null);
                        WaitIdle();
                        WaitDiff(origEtalon > 0);
                        Console.WriteLine($"reset preview: db={vm.EtalonId} previewId={vm.PreviewEtalonId} ok={(vm.EtalonId == origEtalon && vm.PreviewEtalonId is null)} status={vm.Status}");
                        // commit the picked candidate as the etalon
                        vm.SelectedSimilar = vm.SimilarCandidates[0];
                        WaitIdle();
                        vm.PickEtalonCommand.Execute(null);
                        WaitIdle();
                        WaitDiff(true);
                        Console.WriteLine($"picked: etalon={vm.EtalonId} (expected {vm.SimilarCandidates[0].CoinId}) previewId={vm.PreviewEtalonId} status={vm.Status}");
                        // picking must mark the coin as a duplicate and copy the
                        // etalon's non-individual fields (type, year, mint, price)
                        var pickedCoin = db.GetCoin(vm.SimilarCandidates[0].CoinId);
                        var dupCoin = db.GetCoin(vm.CoinId);
                        bool fieldsOk = pickedCoin is not null &&
                            dupCoin is not null &&
                            dupCoin.SourceKind == CoinDetailCompare.App.Models.SourceKind.Duplicate &&
                            dupCoin.TypeId == pickedCoin.TypeId &&
                            dupCoin.MintYear == pickedCoin.MintYear &&
                            dupCoin.Mint == pickedCoin.Mint &&
                            dupCoin.Price == pickedCoin.Price;
                        bool individualKept = dupCoin is not null && snapshot is not null &&
                            dupCoin.AlbumName == snapshot.AlbumName &&
                            dupCoin.PageNo == snapshot.PageNo &&
                            dupCoin.OrderNo == snapshot.OrderNo &&
                            dupCoin.Quality == snapshot.Quality &&
                            dupCoin.IsAnomaly == snapshot.IsAnomaly;
                        Console.WriteLine($"picked fields: isDup(vm)={vm.IsDuplicate} kind={dupCoin?.SourceKind} fieldsOk={fieldsOk} individualKept={individualKept}");
                        // restore the original etalon, then the coin verbatim,
                        // so the catalog data is untouched
                        vm.SelectedSimilar = orig;
                        WaitIdle();
                        vm.PickEtalonCommand.Execute(null);
                        WaitIdle();
                        WaitDiff(origEtalon > 0);
                        Console.WriteLine($"restored: etalon={vm.EtalonId} (orig {origEtalon}) ok={vm.EtalonId == origEtalon}");
                        db.SaveCoin(snapshot);
                    }
                    else
                    {
                        Console.WriteLine($"orig etalon {origEtalon} not in candidate list - pick test skipped");
                    }
                }
                // Ensure the card's hwnd exists before setting it as owner
                // (Window.Owner throws if the owner window has no hwnd yet).
                new System.Windows.Interop.WindowInteropHelper(coinCard).EnsureHandle();
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
            catch (System.Exception ex) { Console.WriteLine("SMOKE-FAIL: " + ex); Environment.Exit(3); }
            // The timer fires every 15 s and the scenario takes longer than that:
            // stop it so the suite runs exactly once (a re-run would collide
            // with app.Shutdown() and fail on main.Show()).
            timer.Stop();
            app.Shutdown();
        };
        timer.Start();
        app.Run();
        return 0;
    }

    /// <summary>Fail the smoke run with a distinct exit code.</summary>
    static void Check(bool cond, string msg)
    {
        if (!cond)
        {
            Console.WriteLine("SMOKE-FAIL: " + msg);
            Environment.Exit(3);
        }
    }

    /// <summary>First TreeViewItem in the visual tree whose DataContext is a
    /// catalog node with the given coin id.</summary>
    static System.Windows.Controls.TreeViewItem? FindCoinRow(DependencyObject root, int coinId)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TreeViewItem tv
                && tv.DataContext is CatalogTreeNode cn && cn.CoinId == coinId)
                return tv;
            var found = FindCoinRow(child, coinId);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>First TextBlock in the visual tree under `root`.</summary>
    static System.Windows.Controls.TextBlock? FindTextBlock(DependencyObject root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock tb) return tb;
            var found = FindTextBlock(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Set a private WPF field (walking base types): used to synthesize
    /// mouse/keyboard event args the public API cannot construct.</summary>
    static void SetWpfField(object o, string name, object value)
    {
        for (Type t = o.GetType(); t is not null; t = t.BaseType)
        {
            var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f is not null) { f.SetValue(o, value); return; }
        }
        throw new Exception("reflection field not found: " + name);
    }

    /// <summary>First button in the visual tree that has a context menu.</summary>
    static System.Windows.Controls.Button? FindButtonWithContextMenu(System.Windows.Media.Visual root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = (System.Windows.Media.Visual)System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Button b && b.ContextMenu != null) return b;
            var r = FindButtonWithContextMenu(child);
            if (r != null) return r;
        }
        return null;
    }
}
