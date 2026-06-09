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
        settings.LearnedBigrams = new Dictionary<string, int>(
            settings.LearnedBigrams.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Contains(' ') && pair.Value > 0),
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
        if (settings.AiPetLeft is < -200 or > 10000)
        {
            settings.AiPetLeft = null;
        }

        if (settings.AiPetTop is < -200 or > 10000)
        {
            settings.AiPetTop = null;
        }

        if (!string.IsNullOrWhiteSpace(settings.AiPetImagePath) && !File.Exists(settings.AiPetImagePath))
        {
            settings.AiPetImagePath = null;
        }

        settings.AiPetFrames = settings.AiPetFrames.Where(File.Exists).ToList();
        settings.AiPetFrameIntervalMs = Math.Clamp(settings.AiPetFrameIntervalMs, 40, 1000);

        if (string.IsNullOrWhiteSpace(settings.AiPetName) ||
            settings.AiPetName.Equals("Pet", StringComparison.OrdinalIgnoreCase) ||
            settings.AiPetName.Equals("Beaver", StringComparison.OrdinalIgnoreCase))
        {
            settings.AiPetName = "Woody";
        }

        settings.OnnxModelPath = string.IsNullOrWhiteSpace(settings.OnnxModelPath)
            ? null
            : settings.OnnxModelPath.Trim();

        settings.IgnoredProjectFolders = settings.IgnoredProjectFolders.Count == 0
            ? Autocorrect.Core.Brain.IndexOptions.DefaultIgnoredFolders()
            : settings.IgnoredProjectFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.MaxIndexedFileSizeKb = Math.Clamp(settings.MaxIndexedFileSizeKb, 16, 2048);
        settings.MaxIndexedFiles = Math.Clamp(settings.MaxIndexedFiles, 50, 20000);
        settings.EmbeddingModel = Autocorrect.Core.Brain.FastEmbedModelCatalog.Coerce(settings.EmbeddingModel);

        if (string.IsNullOrWhiteSpace(settings.OllamaEmbeddingModel))
        {
            settings.OllamaEmbeddingModel = "nomic-embed-text";
        }

        if (string.IsNullOrWhiteSpace(settings.VectorDbProvider))
        {
            settings.VectorDbProvider = "QdrantLocal";
        }

        if (string.IsNullOrWhiteSpace(settings.QdrantUrl))
        {
            settings.QdrantUrl = "http://localhost:6333";
        }

        if (string.IsNullOrWhiteSpace(settings.EmbeddingProvider))
        {
            settings.EmbeddingProvider = "FastEmbed";
        }

        if (string.IsNullOrWhiteSpace(settings.FastEmbedSidecarUrl))
        {
            settings.FastEmbedSidecarUrl = "http://127.0.0.1:8765";
        }

        if (string.IsNullOrWhiteSpace(settings.PythonExecutable))
        {
            settings.PythonExecutable = "python";
        }

        if (string.IsNullOrWhiteSpace(settings.WriterModel))
        {
            settings.WriterModel = "gemma3:4b";
        }

        if (settings.AiModel.Equals("qwen2.5:3b", StringComparison.OrdinalIgnoreCase))
        {
            settings.AiModel = settings.WriterModel;
        }

        settings.EmbeddingBatchSize = Math.Clamp(settings.EmbeddingBatchSize, 1, 128);
        settings.RetrievalTopK = Math.Clamp(settings.RetrievalTopK, 3, 40);
        settings.MaxInitialIndexSeconds = Math.Clamp(settings.MaxInitialIndexSeconds, 30, 900);
        settings.MaxInitialChunks = Math.Clamp(settings.MaxInitialChunks, 100, 100000);

        if (!string.IsNullOrWhiteSpace(settings.ProjectRoot) && !Directory.Exists(settings.ProjectRoot))
        {
            settings.ProjectRoot = null;
        }

        return settings;
    }
}
