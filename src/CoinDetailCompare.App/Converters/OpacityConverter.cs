using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoinDetailCompare.App.Converters;

/// <summary>Converts a percent value (0-100) to an opacity (0-1).</summary>
public class OpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? Math.Clamp(d / 100.0, 0.0, 1.0) : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? Math.Clamp(d * 100.0, 0.0, 100.0) : 0.0;
}
