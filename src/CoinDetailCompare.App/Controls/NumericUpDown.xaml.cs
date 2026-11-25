using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CoinDetailCompare.App.Controls;

/// <summary>
/// Numeric field with up/down spinner arrows stepping by <see cref="Step"/>
/// (default 0.01). The typed value is applied on Enter or when focus is lost;
/// <see cref="Value"/> is a two-way dependency property, so the field stays
/// in sync with external updates (sliders, mouse handlers).
/// </summary>
public partial class NumericUpDown : UserControl
{
    private bool _suppress;

    public NumericUpDown()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumericUpDown),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Step { get; set; } = 0.01;
    public double Minimum { get; set; } = double.NegativeInfinity;
    public double Maximum { get; set; } = double.PositiveInfinity;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (NumericUpDown)d;
        if (c._suppress) return;
        c._suppress = true;
        c.TextBox.Text = ((double)e.NewValue).ToString("0.00", CultureInfo.InvariantCulture);
        c._suppress = false;
    }

    private void OnStepUp(object sender, RoutedEventArgs e) => Apply(Value + Step);
    private void OnStepDown(object sender, RoutedEventArgs e) => Apply(Value - Step);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEdit();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e) => CommitEdit();

    /// <summary>Parses the box text and applies it; an invalid text is reselected.</summary>
    public void CommitEdit()
    {
        if (_suppress) return;
        if (double.TryParse(TextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            && double.IsFinite(v))
            Apply(v);
        else
            TextBox.SelectAll();
    }

    private void Apply(double v)
    {
        v = double.IsFinite(v) ? Math.Clamp(v, Minimum, Maximum) : Value;
        Value = Math.Round(v, 2); // keep 0.01 precision clean
    }
}
