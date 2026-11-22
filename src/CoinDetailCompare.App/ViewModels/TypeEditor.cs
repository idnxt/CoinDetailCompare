using CoinDetailCompare.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoinDetailCompare.App.ViewModels;

/// <summary>Editable type card (coin_type) for the current coin.</summary>
public class TypeEditor : ObservableObject
{
    private int _id;
    private string? _numistaUrl;
    private string? _name;
    private string? _issuer;
    private string? _period;
    private string? _type;
    private string? _years;
    private string? _value;
    private string? _currency;
    private string? _composition;
    private string? _mass;
    private string? _diameter;
    private string? _thickness;
    private string? _shape;
    private string? _technique;
    private string? _orientation;
    private string? _obverseText;
    private string? _reverseText;
    private string? _rimText;

    public int Id { get => _id; private set => SetProperty(ref _id, value); }
    public string? NumistaUrl { get => _numistaUrl; set => SetProperty(ref _numistaUrl, value); }
    public string? Name { get => _name; set => SetProperty(ref _name, value); }
    public string? Issuer { get => _issuer; set => SetProperty(ref _issuer, value); }
    public string? Period { get => _period; set => SetProperty(ref _period, value); }
    public string? Type { get => _type; set => SetProperty(ref _type, value); }
    public string? Years { get => _years; set => SetProperty(ref _years, value); }
    public string? Value { get => _value; set => SetProperty(ref _value, value); }
    public string? Currency { get => _currency; set => SetProperty(ref _currency, value); }
    public string? Composition { get => _composition; set => SetProperty(ref _composition, value); }
    public string? Mass { get => _mass; set => SetProperty(ref _mass, value); }
    public string? Diameter { get => _diameter; set => SetProperty(ref _diameter, value); }
    public string? Thickness { get => _thickness; set => SetProperty(ref _thickness, value); }
    public string? Shape { get => _shape; set => SetProperty(ref _shape, value); }
    public string? Technique { get => _technique; set => SetProperty(ref _technique, value); }
    public string? Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }
    public string? ObverseText { get => _obverseText; set => SetProperty(ref _obverseText, value); }
    public string? ReverseText { get => _reverseText; set => SetProperty(ref _reverseText, value); }
    public string? RimText { get => _rimText; set => SetProperty(ref _rimText, value); }

    public CoinType ToCoinType() => new()
    {
        Id = Id,
        NumistaUrl = NumistaUrl,
        Name = Name,
        Issuer = Issuer,
        Period = Period,
        Type = Type,
        Years = Years,
        Value = Value,
        Currency = Currency,
        Composition = Composition,
        Mass = Mass,
        Diameter = Diameter,
        Thickness = Thickness,
        Shape = Shape,
        Technique = Technique,
        Orientation = Orientation,
        ObverseText = ObverseText,
        ReverseText = ReverseText,
        RimText = RimText,
    };

    public void LoadFrom(CoinType? t)
    {
        if (t is null)
        {
            Clear();
            return;
        }
        Id = t.Id;
        NumistaUrl = t.NumistaUrl;
        Name = t.Name;
        Issuer = t.Issuer;
        Period = t.Period;
        Type = t.Type;
        Years = t.Years;
        Value = t.Value;
        Currency = t.Currency;
        Composition = t.Composition;
        Mass = t.Mass;
        Diameter = t.Diameter;
        Thickness = t.Thickness;
        Shape = t.Shape;
        Technique = t.Technique;
        Orientation = t.Orientation;
        ObverseText = t.ObverseText;
        ReverseText = t.ReverseText;
        RimText = t.RimText;
    }

    public void Clear()
    {
        Id = 0;
        NumistaUrl = null;
        Name = null;
        Issuer = null;
        Period = null;
        Type = null;
        Years = null;
        Value = null;
        Currency = null;
        Composition = null;
        Mass = null;
        Diameter = null;
        Thickness = null;
        Shape = null;
        Technique = null;
        Orientation = null;
        ObverseText = null;
        ReverseText = null;
        RimText = null;
    }
}
