using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Double-click on a catalog tree row opens the coin's card.
    /// The node is read from the clicked item (DataContext), not from
    /// TreeView.SelectedItem — the selection state can lag the click.</summary>
    private void CatalogTreeItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (sender is not TreeViewItem { DataContext: CatalogTreeNode node }) return;
        (DataContext as MainViewModel)?.EditCoinFromTreeCommand.Execute(node);
    }
}
