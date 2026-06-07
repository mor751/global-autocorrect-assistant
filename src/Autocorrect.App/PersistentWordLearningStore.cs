using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class PersistentWordLearningStore : IWordLearningStore
{
    private const int SaveEveryChanges = 8;
    private const int MaxLearnedWords = 2500;

    private readonly CorrectionSettings _settings;
    private readonly SettingsRepository _repository;
    private readonly object _gate = new();
    private int _pendingChanges;

    public PersistentWordLearningStore(CorrectionSettings settings, SettingsRepository repository)
    {
        _settings = settings;
        _repository = repository;
    }

    public void RecordTypedWord(string word)
    {
        if (!_settings.EnableUserLearning || !CanLearn(word))
        {
            return;
        }

        lock (_gate)
        {
            var normalized = word.ToLowerInvariant();
            _settings.LearnedWordFrequencies[normalized] = _settings.LearnedWordFrequencies.GetValueOrDefault(normalized) + 1;
            TrimIfNeeded();
            SaveOccasionally();
        }
    }

    public void RecordAcceptedCorrection(string original, string replacement)
    {
        if (_settings.EnableUserLearning && CanLearn(original) && CanLearn(replacement))
        {
            lock (_gate)
            {
                _settings.LearnedCorrections[original.ToLowerInvariant()] = replacement.ToLowerInvariant();
                SaveOccasionally();
            }
        }

        RecordTypedWord(replacement);
    }

    public void RecordRejectedCorrection(string original, string replacement)
    {
        if (!_settings.EnableUserLearning)
        {
            return;
        }

        lock (_gate)
        {
            var key = $"{original.ToLowerInvariant()}->{replacement.ToLowerInvariant()}";
            _settings.RejectedCorrections[key] = _settings.RejectedCorrections.GetValueOrDefault(key) + 1;
            _settings.IgnoredWords.Add(original);
            SaveOccasionally();
        }
    }

    private void SaveOccasionally()
    {
        _pendingChanges++;
        if (_pendingChanges < SaveEveryChanges)
        {
            return;
        }

        _pendingChanges = 0;
        _repository.Save(_settings);
    }

    private void TrimIfNeeded()
    {
        if (_settings.LearnedWordFrequencies.Count <= MaxLearnedWords)
        {
            return;
        }

        foreach (var word in _settings.LearnedWordFrequencies
                     .OrderBy(pair => pair.Value)
                     .Take(_settings.LearnedWordFrequencies.Count - MaxLearnedWords))
        {
            _settings.LearnedWordFrequencies.Remove(word.Key);
        }
    }

    private static bool CanLearn(string word)
    {
        return word.Length is >= 2 and <= 32 &&
               word.All(c => char.IsLetter(c) || c == '\'');
    }
}
