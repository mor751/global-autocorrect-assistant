using System.Windows;

namespace Autocorrect.App;

public partial class RecentCorrectionsWindow : Window
{
    private readonly RecentCorrectionStore _store;

    public RecentCorrectionsWindow(RecentCorrectionStore store)
    {
        InitializeComponent();
        _store = store;
        Refresh();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Refresh()
    {
        CorrectionsGrid.ItemsSource = _store
            .Snapshot()
            .Select(c => new RecentCorrectionRow(
                c.When.LocalDateTime.ToString("HH:mm:ss"),
                c.Original,
                c.Replacement,
                c.Confidence.ToString("0.00"),
                c.ProcessName,
                c.Reason))
            .ToArray();
    }

    private sealed record RecentCorrectionRow(
        string WhenText,
        string Original,
        string Replacement,
        string ConfidenceText,
        string ProcessName,
        string Reason);
}
