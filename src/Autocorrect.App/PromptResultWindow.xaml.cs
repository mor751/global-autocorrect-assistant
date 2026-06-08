using System.Windows;
using System.Windows.Media;
using Autocorrect.Core.Brain;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Autocorrect.App;

public partial class PromptResultWindow : Window
{
    private readonly EnhancementOutcome _outcome;
    private readonly Func<string, Task> _replaceAction;
    private readonly Func<Task> _reindexAction;
    private readonly Action _openBrainAction;

    public PromptResultWindow(
        EnhancementOutcome outcome,
        Func<string, Task> replaceAction,
        Func<Task> reindexAction,
        Action openBrainAction)
    {
        InitializeComponent();
        _outcome = outcome;
        _replaceAction = replaceAction;
        _reindexAction = reindexAction;
        _openBrainAction = openBrainAction;
        Render();
    }

    // Paints every field of the overlay from the enhancement outcome.
    private void Render()
    {
        var analysis = _outcome.Analysis;
        var result = _outcome.Result;

        ApplyBanner();
        QualityValue.Text = Percent(analysis.QualityScore);
        ClarityValue.Text = Percent(analysis.ClarityScore);
        ContextValue.Text = Percent(analysis.ContextScore);
        RiskValue.Text = analysis.RiskLevel.ToString();
        RiskValue.Foreground = RiskBrush(analysis.RiskLevel);
        TaskTypeValue.Text = analysis.TaskType.ToString();

        OriginalBox.Text = _outcome.OriginalPrompt;
        ImprovedBox.Text = result.Kind == EnhancementKind.ShorterPrompt ? result.ShorterPrompt : result.ImprovedPrompt;
        FilesList.ItemsSource = result.RelevantFiles.Count > 0 ? result.RelevantFiles : new List<string> { "None detected" };
        MissingList.ItemsSource = result.MissingContext.Count > 0 ? result.MissingContext : new List<string> { "Nothing critical missing" };

        var sign = result.EstimatedPromptTokenChange >= 0 ? "+" : string.Empty;
        EffectText.Text = $"Estimated prompt tokens: {sign}{result.EstimatedPromptTokenChange}  ·  " +
                          $"~{result.EstimatedReducedRetries:0.0} fewer retries  ·  " +
                          $"confidence {Percent(result.Confidence)}  ·  " +
                          $"Ollama: {(_outcome.OllamaAvailable ? "on" : "fallback")}";

        ReindexButton.IsEnabled = _outcome.ProjectIndexed || !string.IsNullOrEmpty(_outcome.Brain?.ProjectRoot);
        OpenBrainButton.IsEnabled = _outcome.Brain is not null;
    }

    private void ApplyBanner()
    {
        var (icon, text, color) = _outcome.Status switch
        {
            EnhancementStatus.ImprovedReady => ("\u2728", "Improved prompt ready.", "#22D3EE"),
            EnhancementStatus.MissingContext => ("\u26A0", "Prompt is vague — add the missing context below before sending.", "#F4C77B"),
            EnhancementStatus.NotIndexed => ("\u25CB", "No project indexed. Enhanced generally — select a project folder for smarter results.", "#9FB4C4"),
            EnhancementStatus.OllamaFallback => ("\u26A1", "Ollama unavailable — using local fallback enhancement.", "#F4C77B"),
            _ => ("\u2728", "Done.", "#22D3EE")
        };

        BannerIcon.Text = icon;
        BannerText.Text = text;
        var brush = (Color)ColorConverter.ConvertFromString(color);
        Banner.BorderBrush = new SolidColorBrush(brush);
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        TrySetClipboard(ImprovedBox.Text);
        CopyButton.Content = "Copied";
    }

    private async void Replace_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
        await _replaceAction(ImprovedBox.Text);
        Close();
    }

    private void Shorter_OnClick(object sender, RoutedEventArgs e)
    {
        ImprovedBox.Text = _outcome.Result.ShorterPrompt;
    }

    // Appends a user-provided context block to the editable prompt so the next agent has what it needs.
    private void AddContext_OnClick(object sender, RoutedEventArgs e)
    {
        ImprovedBox.Text += "\n\nAdditional context:\n- ";
        ImprovedBox.Focus();
        ImprovedBox.CaretIndex = ImprovedBox.Text.Length;
    }

    private async void Reindex_OnClick(object sender, RoutedEventArgs e)
    {
        ReindexButton.Content = "Re-indexing…";
        ReindexButton.IsEnabled = false;
        await _reindexAction();
        ReindexButton.Content = "Re-indexed";
    }

    private void OpenBrain_OnClick(object sender, RoutedEventArgs e) => _openBrainAction();

    private static string Percent(double value) => $"{(int)Math.Round(Math.Clamp(value, 0, 1) * 100)}%";

    private static SolidColorBrush RiskBrush(RiskLevel level)
    {
        var hex = level switch
        {
            RiskLevel.Low => "#34D399",
            RiskLevel.Medium => "#F4C77B",
            _ => "#F87171"
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private static void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard can be briefly locked by another process; ignore.
        }
    }
}
