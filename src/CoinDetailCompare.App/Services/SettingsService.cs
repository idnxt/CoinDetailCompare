using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoinDetailCompare.App.Models;

namespace CoinDetailCompare.App.Services;

/// <summary>
/// Persisted display settings of one coin-card layer: visible (checkbox),
/// overlay color and opacity in percent (0-100).
/// </summary>
public class CardLayerSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>#AARRGGBB (red: the diff layer's draft color).</summary>
    public string Color { get; set; } = "#FFFF3030";
    public double Opacity { get; set; } = 50;
}

/// <summary>Application settings (appsettings.json).</summary>
public class AppSettings
{
    public string BackendHost { get; set; } = "127.0.0.1";
    public int BackendPort { get; set; } = 8300;

    public string CoimgDir { get; set; } = @"E:\CoinDetailCompare\coimg";
    public string DbPath { get; set; } = @"E:\CoinDetailCompare\coimg\catalog.db";
    public string SchemaPath { get; set; } = @"E:\CoinDetailCompare\shared\sqlite_schema.sql";

    public DeviceType Device { get; set; } = DeviceType.Gpu;
    public VisionModel Model { get; set; } = VisionModel.VitHPlus16;
    public int TargetSize { get; set; } = 1024;

    public int HeatmapSize { get; set; } = 128;
    public double AnomalyThreshold { get; set; } = 0.5;
    public int NumNormalReferences { get; set; } = 5;

    public string NumistaApiKey { get; set; } = "";
    public string NumistaBaseUrl { get; set; } = "https://www.numista.com/api/v1";

    public string UiLanguage { get; set; } = "ru";
    public List<string> EnabledLanguages { get; set; } = new() { "ru", "en" };

    // Card layers: applied to every opened card, persisted on change.
    public CardLayerSettings LayerDiff { get; set; } = new();
    public CardLayerSettings LayerEtalonPhoto { get; set; } = new();

    [JsonIgnore]
    public string BackendBaseUrl => $"http://{BackendHost}:{BackendPort}";

    /// <summary>Wired by the app to SettingsService.Save; VMs call it after
    /// mutating settings (e.g. the persisted card-layer options).</summary>
    [JsonIgnore]
    public Action? Persist;
}

public class SettingsService
{
    public AppSettings Current { get; private set; }
    public string AppSettingsPath { get; }

    public SettingsService(string baseDir)
    {
        AppSettingsPath = Path.Combine(baseDir, "appsettings.json");
        Current = Load(AppSettingsPath);
    }

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true,
                       Converters = { new JsonStringEnumConverter() },
                   })
               ?? new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            });
        File.WriteAllText(AppSettingsPath, json);
    }
}
