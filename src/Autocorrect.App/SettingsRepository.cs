using System.IO;
using System.Text.Json;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class SettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly string _learnedCorrectionsPath;
    private readonly string _protectedVocabularyPath;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GlobalAutocorrect");

    public SettingsRepository()
    {
        var directory = DataDirectory;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
        _learnedCorrectionsPath = Path.Combine(directory, "learned-corrections.json");
        _protectedVocabularyPath = Path.Combine(directory, "protected-vocabulary.json");
    }

    public CorrectionSettings Load()
    {
        if (!File.Exists(_path))
        {
            var defaults = new CorrectionSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_path);
            return LoadSidecarData(Normalize(JsonSerializer.Deserialize<CorrectionSettings>(json, JsonOptions) ?? new CorrectionSettings()));
        }
        catch
        {
            return new CorrectionSettings();
        }
    }

    public void Save(CorrectionSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
        File.WriteAllText(_learnedCorrectionsPath, JsonSerializer.Serialize(settings.LearnedCorrections, JsonOptions));
        File.WriteAllText(_protectedVocabularyPath, JsonSerializer.Serialize(settings.ProtectedVocabulary.Order(StringComparer.OrdinalIgnoreCase), JsonOptions));
    }

    private CorrectionSettings LoadSidecarData(CorrectionSettings settings)
    {
        try
        {
            if (File.Exists(_learnedCorrectionsPath))
            {
                settings.LearnedCorrections = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_learnedCorrectionsPath),
                    JsonOptions) ?? settings.LearnedCorrections;
            }

            if (File.Exists(_protectedVocabularyPath))
            {
                settings.ProtectedVocabulary = (JsonSerializer.Deserialize<HashSet<string>>(
                    File.ReadAllText(_protectedVocabularyPath),
                    JsonOptions) ?? settings.ProtectedVocabulary).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Sidecar data should never prevent the app from starting.
        }

        return Normalize(settings);
    }

    private static CorrectionSettings Normalize(CorrectionSettings settings)
    {
        settings.BlockedProcesses = settings.BlockedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.EnabledProcesses = settings.EnabledProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.IgnoredWords = settings.IgnoredWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.ProtectedVocabulary = settings.ProtectedVocabulary.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.LearnedWordFrequencies = new Dictionary<string, int>(
            settings.LearnedWordFrequencies.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0),
            StringComparer.OrdinalIgnoreCase);
        settings.LearnedCorrections = new Dictionary<string, string>(
            settings.LearnedCorrections.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)),
            StringComparer.OrdinalIgnoreCase);
        settings.RejectedCorrections = new Dictionary<string, int>(
            settings.RejectedCorrections.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0),
            StringComparer.OrdinalIgnoreCase);
        settings.CustomCorrections = new Dictionary<string, string>(
            settings.CustomCorrections,
            StringComparer.OrdinalIgnoreCase);
        settings.MaxCorrectionLatencyMs = Math.Clamp(settings.MaxCorrectionLatencyMs, 80, 1500);
        settings.ConfidenceThreshold = Math.Clamp(settings.ConfidenceThreshold, 0.70, 0.99);
        settings.SuggestionThreshold = Math.Clamp(settings.SuggestionThreshold, 0.50, 0.99);
        settings.LearnWordAfterCount = Math.Clamp(settings.LearnWordAfterCount, 1, 20);
        settings.FloatingPillDelayMs = Math.Clamp(settings.FloatingPillDelayMs, 300, 2500);
        settings.MinWordsForOverlay = Math.Clamp(settings.MinWordsForOverlay, 3, 40);
        settings.CorrectionHistoryLimit = Math.Clamp(settings.CorrectionHistoryLimit, 20, 5000);
        settings.OnnxModelPath = string.IsNullOrWhiteSpace(settings.OnnxModelPath)
            ? null
            : settings.OnnxModelPath.Trim();
        return settings;
    }
}
