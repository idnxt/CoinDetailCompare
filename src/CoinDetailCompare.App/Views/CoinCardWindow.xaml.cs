using System.Windows;
using CoinDetailCompare.App.Services;
using CoinDetailCompare.App.ViewModels;

namespace CoinDetailCompare.App.Views;

/// <summary>
/// Card of a saved coin: all layers as during photo (base image, heatmap,
/// rotation-normalized diff against the etalon, 2-D vectors, annotations)
/// plus editable catalog attributes.
/// </summary>
public partial class CoinCardWindow : Window
{
    public CoinCardWindow(int coinId, AppSettings settings, BackendClient backend,
        DbService db, LocalizationService loc)
    {
        InitializeComponent();
        var vm = new CoinCardViewModel(coinId, settings, backend, db, loc);
        DataContext = vm;
        Title = string.Format(loc.T("card.title"), coinId);
    }
}
