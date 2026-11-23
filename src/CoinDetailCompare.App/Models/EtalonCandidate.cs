namespace CoinDetailCompare.App.Models;

/// <summary>
/// One etalon candidate from a search result, shown in the list below the
/// etalon photo. Clicking it swaps the candidate in as the etalon.
/// </summary>
public sealed class EtalonCandidate
{
    public int CoinId { get; init; }
    public int? TypeId { get; init; }
    public string? Quality { get; init; }
    public double ObversePct { get; init; }
    public double ReversePct { get; init; }
    public double OverallPct { get; init; }
    public string Orientation { get; init; } = "";
    public string? EtalonObverse { get; init; }
    public string? EtalonReverse { get; init; }
    public string Label { get; init; } = "";
}
