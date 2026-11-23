using System.Collections.ObjectModel;
using CoinDetailCompare.App.Models;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoinDetailCompare.App.ViewModels;

/// <summary>
/// Catalog of saved coins (list for now): code А.С.Н (album.page.number),
/// kind letter (Э эталон / Д дубль / А аномалия) and the record name.
/// Clicking "edit" opens the coin's card with all layers, as during photo.
/// </summary>
public class CatalogViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly BackendClient _backend;
    private readonly DbService _db;
    private readonly LocalizationService _loc;

    private ObservableCollection<CatalogItem> _items = new();
    private string _status = "";

    public CatalogViewModel(AppSettings settings, BackendClient backend, DbService db, LocalizationService loc)
    {
        _settings = settings;
        _backend = backend;
        _db = db;
        _loc = loc;
        RefreshCommand = new RelayCommand(Refresh);
        EditCommand = new RelayCommand<CatalogItem>(Edit);
    }

    public ObservableCollection<CatalogItem> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand<CatalogItem> EditCommand { get; }

    public string T(string key) => _loc.T(key);

    /// <summary>Reload the catalog from the database (А.С.Н order).</summary>
    public void Refresh()
    {
        var items = _db.ListAllCoins().Select(BuildItem).ToList();
        Items = new ObservableCollection<CatalogItem>(items);
        Status = string.Format(T("catalog.count"), items.Count);
    }

    /// <summary>Open the card of the chosen coin (all layers, as during photo).</summary>
    public void Edit(CatalogItem? item)
    {
        if (item is null) return;
        var win = new CoinCardWindow(item.Id, _settings, _backend, _db, _loc)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        win.ShowDialog();
    }

    private CatalogItem BuildItem(Coin c)
    {
        var name = c.TypeId is int tid ? _db.GetCoinType(tid)?.Name : null;
        return new CatalogItem
        {
            Id = c.Id,
            Code = BuildCode(c),
            Kind = KindFor(c),
            Name = string.IsNullOrWhiteSpace(name) ? $"#{c.Id}" : name!,
            Year = c.MintYear ?? "",
            Quality = c.Quality ?? "",
            EtalonId = c.EtalonId,
        };
    }

    /// <summary>А.С.Н: album.page.number; empty parts are skipped.</summary>
    private static string BuildCode(Coin c)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.AlbumName)) parts.Add(c.AlbumName!);
        if (c.PageNo is int p) parts.Add(p.ToString());
        if (c.OrderNo is int o) parts.Add(o.ToString());
        return parts.Count > 0 ? string.Join(".", parts) : "—";
    }

    /// <summary>Буква вида: Э — эталон, Д — дубль/вариант; флаг аномалии
    /// показывается отдельно (эталон может быть и аномалией → «Э А»).</summary>
    public static string KindFor(Coin c)
    {
        var kind = c.SourceKind is SourceKind.Duplicate or SourceKind.Variant or SourceKind.Anomaly
            ? "Д"
            : "Э";
        return c.IsAnomaly ? $"{kind} А" : kind;
    }
}
