namespace CoinDetailCompare.App.Models;

/// <summary>Индивидуальная запись монеты.</summary>
public class Coin
{
    public int Id { get; set; }
    public int? TypeId { get; set; }

    public string? MintYear { get; set; }
    public string? Mint { get; set; }        // монетный двор
    public string? Quality { get; set; }     // G VG F VF XF AU UNC
    public double Price { get; set; }        // по умолчанию 0

    public string? AlbumName { get; set; }
    public int? PageNo { get; set; }
    public int? OrderNo { get; set; }

    public string? ObversePhoto { get; set; }
    public string? ReversePhoto { get; set; }
    public string? ObverseHeatmap { get; set; }
    public string? ReverseHeatmap { get; set; }
    public string? ObverseLayer { get; set; }   // JSON, норм. координаты 0.0..1.0
    public string? ReverseLayer { get; set; }
    public string? ObverseVector { get; set; }  // /coimg/{id}_obverse.npy
    public string? ReverseVector { get; set; }

    public bool IsAnomaly { get; set; }
    public string? AnomalyDesc { get; set; }

    public string? NumistaUrl { get; set; }
    public SourceKind SourceKind { get; set; } = SourceKind.Match;
    public int? EtalonId { get; set; }
    public DateTime CreatedAt { get; set; }
}
