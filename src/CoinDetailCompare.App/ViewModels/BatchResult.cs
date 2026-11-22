using CommunityToolkit.Mvvm.ComponentModel;

namespace CoinDetailCompare.App.ViewModels;

/// <summary>Phase of the batch capture wizard.</summary>
public enum BatchPhase
{
    Idle,
    Obverse,   // shooting all obverses first
    Reverse,   // then all reverses, same order
    Done,
}

/// <summary>One captured batch pair (obverse + reverse of the same coin).</summary>
public class BatchResult : ObservableObject
{
    public int OrderNo { get; init; }
    public string? ObversePath { get; set; }
    public string? ReversePath { get; set; }

    private double _obversePct;
    private double _reversePct;
    private int? _topCoinId;
    private string _orientation = "";
    private string _status = "";

    public double ObversePct { get => _obversePct; set => SetProperty(ref _obversePct, value); }
    public double ReversePct { get => _reversePct; set => SetProperty(ref _reversePct, value); }
    public int? TopCoinId { get => _topCoinId; set => SetProperty(ref _topCoinId, value); }
    public string Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
}
