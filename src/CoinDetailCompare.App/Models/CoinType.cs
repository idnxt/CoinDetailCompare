namespace CoinDetailCompare.App.Models;

/// <summary>Общая карточка серии/типа монеты (справка). Хранится один раз.</summary>
public class CoinType
{
    public int Id { get; set; }
    public string? NumistaN { get; set; }        // N#966
    public string? NumistaUrl { get; set; }      // https://ru.numista.com/966
    public string? Name { get; set; }           // 1 New Dollar
    public string? Issuer { get; set; }
    public string? Period { get; set; }
    public string? Type { get; set; }
    public string? Years { get; set; }
    public string? Calendar { get; set; }
    public string? Value { get; set; }
    public string? Rate { get; set; }
    public string? Currency { get; set; }
    public string? Composition { get; set; }
    public string? Mass { get; set; }
    public string? Diameter { get; set; }
    public string? Thickness { get; set; }
    public string? Shape { get; set; }
    public string? Technique { get; set; }
    public string? Orientation { get; set; }
    public string? IssuedDate { get; set; }
    public string? Links { get; set; }          // Y#551, Schön#21, PCGS#..., ...
    public string? ObverseText { get; set; }
    public string? ReverseText { get; set; }
    public string? RimText { get; set; }
    public DateTime CreatedAt { get; set; }
}
