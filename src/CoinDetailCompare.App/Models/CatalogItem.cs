namespace CoinDetailCompare.App.Models;

/// <summary>
/// One row of the saved-coins catalog list: catalog code (album.page.number),
/// kind letter (Э эталон / Д дубль / А аномалия) and the record name.
/// </summary>
public sealed class CatalogItem
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Year { get; init; } = "";
    public string Quality { get; init; } = "";
    public int? EtalonId { get; init; }
}
