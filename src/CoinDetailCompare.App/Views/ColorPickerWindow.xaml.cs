using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CoinDetailCompare.App.Views;

/// <summary>Small color picker: preset swatches plus free R/G/B entry.</summary>
public partial class ColorPickerWindow : Window
{
    private static readonly Color[] Presets =
    {
        Color.FromRgb(0x00, 0xE5, 0xFF), // cyan (default)
        Color.FromRgb(0xFF, 0x66, 0x66), // red
        Color.FromRgb(0x66, 0xFF, 0x66), // green
        Color.FromRgb(0x66, 0x99, 0xFF), // blue
        Color.FromRgb(0xFF, 0xCC, 0x00), // amber
        Color.FromRgb(0xCC, 0x66, 0xFF), // violet
        Color.FromRgb(0xFF, 0x00, 0xFF), // magenta
        Color.FromRgb(0x00, 0xFF, 0x00), // lime
        Color.FromRgb(0xFF, 0xFF, 0x66), // yellow
        Color.FromRgb(0xFF, 0x99, 0x00), // orange
        Color.FromRgb(0x33, 0xCC, 0x33), // spring green
        Color.FromRgb(0x33, 0xCC, 0xCC), // teal
        Color.FromRgb(0xFF, 0xFF, 0xFF), // white
        Color.FromRgb(0xFF, 0x33, 0x99), // pink
        Color.FromRgb(0x99, 0x99, 0xFF), // periwinkle
        Color.FromRgb(0xCC, 0xCC, 0x33), // olive
    };

    private Color _current;
    // True until the constructor has finished: XAML sets Text="0" on the first
    // box before the other two exist, which would raise TextChanged into
    // half-built controls (NullReferenceException).
    private bool _suppressRgba = true;

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        _current = initial;
        foreach (var c in Presets)
        {
            var swatch = new Button
            {
                Width = 30,
                Height = 20,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(c),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Tag = c,
            };
            swatch.Click += (_, _) => SetColor((Color)swatch.Tag!);
            SwatchGrid.Children.Add(swatch);
        }
        SetColor(initial);
        _suppressRgba = false;
    }

    public Color PickedColor => _current;

    private void SetColor(Color c)
    {
        _current = c;
        _suppressRgba = true;
        RedBox.Text = c.R.ToString(CultureInfo.InvariantCulture);
        GreenBox.Text = c.G.ToString(CultureInfo.InvariantCulture);
        BlueBox.Text = c.B.ToString(CultureInfo.InvariantCulture);
        _suppressRgba = false;
        PreviewBox.Background = new SolidColorBrush(c);
        HexText.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void OnRgbaChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressRgba) return;
        if (!TryParse(out var c)) return;
        SetColor(c);
    }

    private bool TryParse(out Color c)
    {
        c = _current;
        if (RedBox is null || GreenBox is null || BlueBox is null) return false;
        bool ok = true;
        byte r = 0, g = 0, b = 0;
        ok &= TryByte(RedBox.Text, out r);
        ok &= TryByte(GreenBox.Text, out g);
        ok &= TryByte(BlueBox.Text, out b);
        if (ok) c = Color.FromRgb(r, g, b);
        return ok;

        static bool TryByte(string s, out byte v)
        {
            v = 0;
            return byte.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                && v <= 255;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        // ShowDialog() returns DialogResult; without it the pick is dropped.
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
