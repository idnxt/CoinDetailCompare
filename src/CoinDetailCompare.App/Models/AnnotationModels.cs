using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace CoinDetailCompare.App.Models;

/// <summary>Annotation tool.</summary>
public enum AnnotationTool
{
    Path,
    Ellipse,
    Arrow,
}

/// <summary>
/// One vector annotation. Coordinates are normalized to 0.0..1.0
/// relative to the image (independent of display size).
/// </summary>
public class AnnotationItem
{
    public string Shape { get; set; } = "path"; // path | ellipse | arrow
    public List<float> Points { get; set; } = new(); // normalized x,y pairs
    public string Color { get; set; } = "#FFFF3030";
    public int ZOrder { get; set; }
}

/// <summary>Vector layer file: /coimg/{id}_{side}_layer.json.</summary>
public class AnnotationLayer
{
    public int CoinId { get; set; }
    public string Side { get; set; } = "obverse";
    public List<AnnotationItem> Items { get; set; } = new();

    public static AnnotationLayer Load(string path)
    {
        if (!File.Exists(path)) return new AnnotationLayer();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AnnotationLayer>(json) ?? new AnnotationLayer();
    }

    public static string Save(int coinId, string side, List<AnnotationItem> items, string coimgDir)
    {
        var layer = new AnnotationLayer { CoinId = coinId, Side = side, Items = items };
        var path = Path.Combine(coimgDir, $"{coinId}_{side}_layer.json");
        var json = JsonSerializer.Serialize(layer, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }
}
