using System.Windows;

namespace Autocorrect.App;

public partial class DashboardWindow : Window
{
    private readonly TokenUsageStore _store;

    public DashboardWindow(TokenUsageStore store)
    {
        InitializeComponent();
        _store = store;
        Loaded += (_, _) => Refresh();
    }

    // Pulls the latest analytics snapshot and repaints every card and the recent list.
    public void Refresh()
    {
        var snapshot = _store.Snapshot();
        var saved = Math.Max(0, snapshot.TotalTokensBefore - snapshot.TotalTokensAfter);
        var avg = snapshot.TotalTokensBefore <= 0
            ? 0
            : Math.Max(0, (int)Math.Round((1 - snapshot.TotalTokensAfter / (double)snapshot.TotalTokensBefore) * 100));

        PromptsValue.Text = snapshot.TotalRewrites.ToString("N0");
        TokensSavedValue.Text = saved.ToString("N0");
        AvgReductionValue.Text = $"{avg}%";
        InOutValue.Text = $"{snapshot.TotalTokensBefore:N0} / {snapshot.TotalTokensAfter:N0}";

        RecentList.ItemsSource = snapshot.Recent.Select(entry => new
        {
            Action = Humanize(entry.Action),
            When = entry.TimestampUtc.ToLocalTime().ToString("MMM d, HH:mm"),
            Flow = $"{entry.TokensBefore} \u2192 {entry.TokensAfter} tok",
            Saved = $"-{entry.TokensSaved} ({entry.ReductionPercent}%)"
        }).ToList();

        EmptyHint.Visibility = snapshot.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => Refresh();

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "Clear all token compression stats? This cannot be undone.",
            "Reset stats",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
        {
            _store.Reset();
            Refresh();
        }
    }

    private static string Humanize(string action)
    {
        return action switch
        {
            "SmartOptimize" => "Improve & compress",
            "OptimizePrompt" => "Optimize + compress",
            "CompressTokens" => "Compress tokens",
            "FixTyposOnly" => "Fix typos",
            "MakeProfessional" => "Make professional",
            "MakeDirect" => "Make direct",
            "CursorCodingPrompt" => "Cursor coding prompt",
            "VideoGenerationPrompt" => "Video prompt",
            "ImproveClarity" => "Improve clarity",
            _ => action
        };
    }
}
