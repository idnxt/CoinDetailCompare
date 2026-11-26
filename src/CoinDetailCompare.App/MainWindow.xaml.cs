using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // ---- catalog tree: double-click / Enter / right-click -----------------
    // All three use tunneling (Preview) events on the TreeView so they fire
    // regardless of which child element (the row's TextBlock) actually
    // received the input; the node is read from the clicked item's
    // DataContext, not from TreeView.SelectedItem, which can lag the click.

    /// <summary>Double-click on a coin row opens its card.</summary>
    private void CatalogTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        var node = NodeAt(e.OriginalSource as DependencyObject);
        if (node is null || node.IsLocked || node.CoinId is null) return;
        (DataContext as MainViewModel)?.EditCoinFromTreeCommand.Execute(node);
        e.Handled = true;
    }

    /// <summary>Enter on a focused coin row opens its card (a plain
    /// double-click alternative for keyboard work).</summary>
    private void CatalogTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var node = NodeAt(e.OriginalSource as DependencyObject);
        if (node is null || node.IsLocked || node.CoinId is null) return;
        (DataContext as MainViewModel)?.EditCoinFromTreeCommand.Execute(node);
        e.Handled = true; // suppress TreeViewItem's native Enter handling
    }

    /// <summary>Right-click on a coin row pops the coin menu (Open/Delete);
    /// other nodes keep the default (no) context menu.</summary>
    private void CatalogTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindParentItem(e.OriginalSource as DependencyObject);
        if (item is null) return;
        if (item.DataContext is not CatalogTreeNode node
            || node.IsLocked || node.CoinId is null) return;
        var vm = DataContext as MainViewModel;
        if (vm is null) return;
        var strings = FindResource("Strings") as Strings;
        if (strings is null) return;

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = strings["catalog.menu_open"],
            Command = vm.EditCoinFromTreeCommand,
            CommandParameter = node,
        });
        menu.Items.Add(new MenuItem
        {
            Header = strings["catalog.menu_delete"],
            Command = vm.DeleteCoinFromTreeCommand,
            CommandParameter = node,
        });
        item.ContextMenu = menu;
    }

    /// <summary>The catalog node of the row containing `source`, if any.</summary>
    private static CatalogTreeNode? NodeAt(DependencyObject? source)
    {
        var item = FindParentItem(source);
        return item is null ? null : item.DataContext as CatalogTreeNode;
    }

    /// <summary>Walk up the visual tree to the nearest TreeViewItem.</summary>
    private static TreeViewItem? FindParentItem(DependencyObject? source)
    {
        var d = source;
        while (d is not null && d is not TreeViewItem)
            d = VisualTreeHelper.GetParent(d);
        return d as TreeViewItem;
    }
}
