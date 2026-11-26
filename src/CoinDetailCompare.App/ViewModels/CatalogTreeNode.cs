using System.Collections.ObjectModel;

namespace CoinDetailCompare.App.ViewModels;

/// <summary>
/// Node of the catalog tree on the main window:
/// "Альбомы" → "Альбом {имя}" → "Страница {N}" → coin
/// (номер, вид Э/Д/А, наименование, год). A coin node carries the coin id
/// and opens its card on double-click; "Страны" is locked until the
/// reference data (countries) is added.
/// </summary>
public class CatalogTreeNode
{
    public string Title { get; set; } = "";

    /// <summary>Set on leaf coin nodes; double-click opens the card.</summary>
    public int? CoinId { get; set; }

    /// <summary>Stable identity of the node (same across rebuilds), used to
    /// restore the expansion state after RefreshCatalogTree.</summary>
    public string Key { get; set; } = "";

    /// <summary>Two-way bound to TreeViewItem.IsExpanded; a refresh keeps
    /// the branches the user had opened.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>Locked (disabled) branch, e.g. "Страны" for now.</summary>
    public bool IsLocked { get; set; }

    public ObservableCollection<CatalogTreeNode> Children { get; } = new();
}
