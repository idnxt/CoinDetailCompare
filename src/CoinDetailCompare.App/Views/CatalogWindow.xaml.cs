using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoinDetailCompare.App.Models;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App.Views;

/// <summary>
/// List of saved coins (code А.С.Н, kind Э/Д/А, name). Refreshes itself on
/// open and every time the window is re-activated, so coins saved in the
/// main window appear without manual action.
/// </summary>
public partial class CatalogWindow : Window
{
    public CatalogWindow(AppSettings settings, BackendClient backend, DbService db, LocalizationService loc)
    {
        InitializeComponent();
        var vm = new CatalogViewModel(settings, backend, db, loc);
        DataContext = vm;
        vm.Refresh();
        Activated += (_, _) => vm.Refresh();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var grid = sender as DataGrid;
        if (grid is null || grid.SelectedIndex < 0 || DataContext is not CatalogViewModel vm)
            return;
        if (grid.ItemContainerGenerator.ContainerFromIndex(grid.SelectedIndex) is not DataGridRow row)
            return;
        if (row.Item is CatalogItem item)
            vm.EditCommand.Execute(item);
    }
}
