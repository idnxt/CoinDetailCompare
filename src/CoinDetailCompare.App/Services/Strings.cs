using System.ComponentModel;
using System.Collections.Generic;

namespace CoinDetailCompare.App.Services;

/// <summary>
/// Bindable view over <see cref="LocalizationService"/>.
///
/// Every localization key is exposed through a string indexer so XAML can bind
/// arbitrary labels, e.g.:
///   Text="{Binding ['capture.album'], Source={StaticResource Strings}}"
///
/// When the language changes, PropertyChanged is raised for every key,
/// so all bound labels update in place.
/// </summary>
public class Strings : INotifyPropertyChanged
{
    private readonly LocalizationService _loc;

    public Strings(LocalizationService loc)
    {
        _loc = loc;
    }

    public string this[string key] => _loc.T(key);

    public void SetLanguage(string lang)
    {
        _loc.SetLanguage(lang);
        foreach (var key in _loc.Keys)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
