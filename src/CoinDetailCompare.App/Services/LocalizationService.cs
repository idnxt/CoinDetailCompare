using System.IO;
using System.Text.Json;

namespace CoinDetailCompare.App.Services;

/// <summary>
/// Multilingual UI strings. Keys use dotted namespaces (e.g. "capture.shoot_obverse").
/// Files live in Resources/i18n/{lang}.json. Default language: ru.
/// </summary>
public class LocalizationService
{
    private readonly string _i18nDir;
    private Dictionary<string, string> _current = new();
    public string CurrentLanguage { get; private set; } = "ru";

    public LocalizationService(string i18nDir)
    {
        _i18nDir = i18nDir;
        Load("ru");
    }

    public void SetLanguage(string lang)
    {
        if (!File.Exists(Path.Combine(_i18nDir, $"{lang}.json")))
            lang = "ru";
        Load(lang);
        CurrentLanguage = lang;
    }

    private void Load(string lang)
    {
        var path = Path.Combine(_i18nDir, $"{lang}.json");
        var dict = new Dictionary<string, string>();
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            Flatten(doc.RootElement, "", dict);
        }
        _current = dict;
    }

    private static void Flatten(JsonElement el, string prefix, Dictionary<string, string> into)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                    Flatten(p.Value, prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}", into);
                break;
            case JsonValueKind.String:
                into[prefix] = el.GetString()!;
                break;
        }
    }

    public string T(string key)
    {
        if (_current.TryGetValue(key, out var val))
            return val;
        // Fallback to default language values if present.
        return key;
    }
}
