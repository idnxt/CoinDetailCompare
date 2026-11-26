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

    /// <summary>Double-click on a coin row of the catalog tree opens its card.</summary>
    private void CatalogTree_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        var node = ((TreeView)sender).SelectedItem as CatalogTreeNode;
        if (node is null) return;
        (DataContext as MainViewModel)?.EditCoinFromTreeCommand.Execute(node);
    }
}
