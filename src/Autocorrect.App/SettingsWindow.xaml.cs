using System.Windows;
using System.Windows.Threading;
using Autocorrect.Core;

namespace Autocorrect.App;

public partial class SettingsWindow : Window
{
    private readonly CorrectionSettings _settings;
    private readonly SettingsRepository _repository;
    private readonly RuntimeStatusStore _runtimeStatus;
    private readonly DispatcherTimer _statusTimer;

    public SettingsWindow(CorrectionSettings settings, SettingsRepository repository, RuntimeStatusStore runtimeStatus)
    {
        InitializeComponent();
        _settings = settings;
        _repository = repository;
        _runtimeStatus = runtimeStatus;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        LoadSettings();
        RefreshStatus();
        _statusTimer.Start();
    }

    private void LoadSettings()
    {
        EnabledCheckBox.IsChecked = _settings.Enabled;
        AutocorrectEnabledCheckBox.IsChecked = _settings.AutocorrectEnabled;
        AiOverlayEnabledCheckBox.IsChecked = _settings.AiOverlayEnabled;
        EnableLearningCheckBox.IsChecked = _settings.EnableUserLearning;
        LearnAfterSlider.Value = _settings.LearnWordAfterCount;
        LearnAfterValue.Text = $"Trust personal word after: {_settings.LearnWordAfterCount} uses";
        LearnedWordsValue.Text = $"Learned words: {_settings.LearnedWordFrequencies.Count}";
        ShowSuggestionPopupCheckBox.IsChecked = _settings.ShowSuggestionPopup;
        EnableOnnxCheckBox.IsChecked = _settings.EnableOnnxFallback;
        OnnxModelPathTextBox.Text = _settings.OnnxModelPath ?? string.Empty;
        UseClipboardFallbackCheckBox.IsChecked = _settings.UseClipboardFallback;
        DeveloperModeCheckBox.IsChecked = _settings.DeveloperModeEnabled;
        ShowFloatingPillCheckBox.IsChecked = _settings.ShowFloatingPill;
        LocalOnlyModeCheckBox.IsChecked = _settings.LocalOnlyMode;
        AiEndpointTextBox.Text = _settings.AiEndpoint;
        AiProviderComboBox.Text = _settings.AiProvider;
        FloatingDelaySlider.Value = _settings.FloatingPillDelayMs;
        MinWordsSlider.Value = _settings.MinWordsForOverlay;
        FloatingDelayValue.Text = $"Floating pill delay: {_settings.FloatingPillDelayMs} ms";
        MinWordsValue.Text = $"Minimum words for overlay: {_settings.MinWordsForOverlay}";
        ThresholdSlider.Value = _settings.ConfidenceThreshold;
        ThresholdValue.Text = _settings.ConfidenceThreshold.ToString("0.00");
        MaxLatencySlider.Value = _settings.MaxCorrectionLatencyMs;
        MaxLatencyValue.Text = $"Max correction latency: {_settings.MaxCorrectionLatencyMs} ms";
        BlockedProcessesTextBox.Text = string.Join(Environment.NewLine, _settings.BlockedProcesses.Order(StringComparer.OrdinalIgnoreCase));
        IgnoredWordsTextBox.Text = string.Join(Environment.NewLine, _settings.IgnoredWords.Order(StringComparer.OrdinalIgnoreCase));
        ProtectedVocabularyTextBox.Text = string.Join(Environment.NewLine, _settings.ProtectedVocabulary.Order(StringComparer.OrdinalIgnoreCase));
        LearnedWordsTextBox.Text = string.Join(
            Environment.NewLine,
            _settings.LearnedWordFrequencies
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        CustomCorrectionsTextBox.Text = string.Join(
            Environment.NewLine,
            _settings.CustomCorrections
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.Enabled = EnabledCheckBox.IsChecked == true;
        _settings.AutocorrectEnabled = AutocorrectEnabledCheckBox.IsChecked == true;
        _settings.AiOverlayEnabled = AiOverlayEnabledCheckBox.IsChecked == true;
        _settings.EnableUserLearning = EnableLearningCheckBox.IsChecked == true;
        _settings.LearnWordAfterCount = (int)Math.Round(LearnAfterSlider.Value);
        _settings.ShowSuggestionPopup = ShowSuggestionPopupCheckBox.IsChecked == true;
        _settings.EnableOnnxFallback = EnableOnnxCheckBox.IsChecked == true;
        _settings.OnnxModelPath = string.IsNullOrWhiteSpace(OnnxModelPathTextBox.Text)
            ? null
            : OnnxModelPathTextBox.Text.Trim();
        _settings.UseClipboardFallback = UseClipboardFallbackCheckBox.IsChecked == true;
        _settings.DeveloperModeEnabled = DeveloperModeCheckBox.IsChecked == true;
        _settings.ShowFloatingPill = ShowFloatingPillCheckBox.IsChecked == true;
        _settings.LocalOnlyMode = LocalOnlyModeCheckBox.IsChecked == true;
        _settings.AiProvider = string.IsNullOrWhiteSpace(AiProviderComboBox.Text) ? "local" : AiProviderComboBox.Text.Trim();
        _settings.AiEndpoint = string.IsNullOrWhiteSpace(AiEndpointTextBox.Text) ? "http://localhost:11434" : AiEndpointTextBox.Text.Trim();
        _settings.FloatingPillDelayMs = (int)Math.Round(FloatingDelaySlider.Value);
        _settings.MinWordsForOverlay = (int)Math.Round(MinWordsSlider.Value);
        _settings.ConfidenceThreshold = Math.Round(ThresholdSlider.Value, 2);
        _settings.MaxCorrectionLatencyMs = (int)Math.Round(MaxLatencySlider.Value);
        _settings.BlockedProcesses = ParseSet(BlockedProcessesTextBox.Text);
        _settings.IgnoredWords = ParseSet(IgnoredWordsTextBox.Text);
        _settings.ProtectedVocabulary = ParseSet(ProtectedVocabularyTextBox.Text);
        _settings.LearnedWordFrequencies = ParseLearnedWords(LearnedWordsTextBox.Text);
        _settings.CustomCorrections = ParseCorrections(CustomCorrectionsTextBox.Text);

        _repository.Save(_settings);
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void ThresholdSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThresholdValue is not null)
        {
            ThresholdValue.Text = $"Minimum confidence: {e.NewValue:0.00}";
        }
    }

    private void MaxLatencySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxLatencyValue is not null)
        {
            MaxLatencyValue.Text = $"Max correction latency: {(int)e.NewValue} ms";
        }
    }

    private void LearnAfterSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LearnAfterValue is not null)
        {
            LearnAfterValue.Text = $"Trust personal word after: {(int)e.NewValue} uses";
        }
    }

    private void FloatingDelaySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FloatingDelayValue is not null)
        {
            FloatingDelayValue.Text = $"Floating pill delay: {(int)e.NewValue} ms";
        }
    }

    private void MinWordsSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinWordsValue is not null)
        {
            MinWordsValue.Text = $"Minimum words for overlay: {(int)e.NewValue}";
        }
    }

    private void ClearLearned_OnClick(object sender, RoutedEventArgs e)
    {
        LearnedWordsTextBox.Clear();
        _settings.LearnedWordFrequencies.Clear();
        _settings.LearnedCorrections.Clear();
        _settings.RejectedCorrections.Clear();
        LearnedWordsValue.Text = "Learned words: 0";
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        base.OnClosed(e);
    }

    private void RefreshStatus()
    {
        var snapshot = _runtimeStatus.Snapshot();
        InputEventsValue.Text = snapshot.InputEvents.ToString();
        CorrectionsValue.Text = snapshot.Corrections.ToString();
        SlowSkipsValue.Text = snapshot.SkippedSlowCorrections.ToString();
        ErrorsValue.Text = snapshot.Errors.ToString();
        LastErrorText.Text = string.IsNullOrWhiteSpace(snapshot.LastError)
            ? "No runtime errors recorded."
            : $"Last error: {snapshot.LastError}";
    }

    private static HashSet<string> ParseSet(string value)
    {
        return value
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseCorrections(string value)
    {
        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in value.Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            corrections[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return corrections;
    }

    private static Dictionary<string, int> ParseLearnedWords(string value)
    {
        var words = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in value.Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            if (int.TryParse(line[(separator + 1)..].Trim(), out var count) && count > 0)
            {
                words[line[..separator].Trim()] = count;
            }
        }

        return words;
    }
}
