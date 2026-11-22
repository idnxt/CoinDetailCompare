using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoinDetailCompare.App.Converters;

/// <summary>Formats a numeric value as a percent, e.g. 92.3 -> "92.3%".</summary>
public class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return d.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        return value is null ? string.Empty : value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
