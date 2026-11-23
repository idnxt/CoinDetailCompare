namespace CoinDetailCompare.App.Models;

/// <summary>
/// One point of the 2-D search-vector projection shown in the comparison
/// panel (query coin + etalon candidates). X/Y are normalized 0..1
/// coordinates; Left/Top map them onto the fixed 260x160 canvas.
/// </summary>
public sealed class VectorPointItem
{
    public double X { get; init; }
    public double Y { get; init; }
    public string Label { get; init; } = "";
    public bool IsQuery { get; init; }

    public double Left => 12 + X * 220;
    public double Top => 12 + Y * 120;
    public string Fill => IsQuery ? "#FFFFD34D" : "#FF5DA9FF";
}
