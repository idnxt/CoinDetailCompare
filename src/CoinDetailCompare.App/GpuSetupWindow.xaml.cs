using System.Windows;

namespace CoinDetailCompare.App;

/// <summary>
/// Console-style log window for the in-app GPU setup (pip install torch cu128,
/// driver check, backend restart). The ViewModel appends lines from a
/// background process stream.
/// </summary>
public partial class GpuSetupWindow : Window
{
    public GpuSetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>Append a log line (thread-safe).</summary>
    public void AppendLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line.Length == 0 ? "\n" : line + "\n");
            LogBox.ScrollToEnd();
        });
    }

    /// <summary>Set the bottom status line (thread-safe).</summary>
    public void SetStatus(string text)
    {
        Dispatcher.Invoke(() => StatusLine.Text = text);
    }
}
