using System.Globalization;

namespace Autocorrect.Core;

public class SymSpellCorrectionEngine : ICorrectionEngine, IAsyncCorrectionEngine, IWordSuggestionEngine
{
    private const int MaxEditDistance = 2;
    private const int PrefixLength = 7;

    private static readonly Lazy<SymSpellIndex> Index = new(() => SymSpellIndex.Build(EnglishFrequencyDictionary.All));

    private static readonly Dictionary<string, string> BuiltInCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["teh"] = "the",
        ["thw"] = "the",
        ["ti"] = "it",
        ["ot"] = "to",
        ["fo"] = "of",
        ["adn"] = "and",
        ["wrod"] = "word",
        ["wrods"] = "words",
        ["wrtiee"] = "write",
        ["writtee"] = "write",
        ["writie"] = "write",
        ["writng"] = "writing",
        ["wrting"] = "writing",
        ["exmple"] = "example",
        ["copmle"] = "complete",
        ["nnotworkigng"] = "not working",
        ["ntio"] = "not",
        ["fnot"] = "not",
        ["ood"] = "good",
        ["coumputer"] = "computer",
        ["compuyter"] = "computer",
        ["compomment"] = "component",
        ["componment"] = "component",
        ["keybroad"] = "keyboard",
        ["keybaord"] = "keyboard",
        ["insted"] = "instead",
        ["automatcly"] = "automatically",
        ["automaticaly"] = "automatically",
        ["backgounrs"] = "background",
        ["buidl"] = "build",
        ["buildd"] = "build",
        ["liek"] = "like",
        ["emant"] = "meant",
        ["stnad"] = "stand",
        ["remveri"] = "remember",
        ["wroking"] = "working",
        ["dotn"] = "don't",
        ["dont"] = "don't",
        ["wnat"] = "want",
        ["nwo"] = "now",
        ["jsut"] = "just",
        ["confgiartiuon"] = "configuration",
        ["sue"] = "use",
        ["ollam"] = "ollama",
        ["samll"] = "small",
        ["pythoin"] = "python",
        ["ptyonh"] = "python",
        ["tensorfloe"] = "tensorflow",
        ["librires"] = "libraries",
        ["soemthgin"] = "something",
        ["msooth"] = "smooth",
        ["msot"] = "most",
        ["imporotnat"] = "important",
        ["seocnds"] = "seconds",
        ["mdole"] = "model",
        ["collaspe"] = "collapse",
        ["stkac"] = "stuck",
        ["fell"] = "feel",
        ["nto"] = "not",
        ["maek"] = "make",
        ["amek"] = "make",
        ["hwo"] = "how",
        ["wht"] = "what",
        ["nmsiektae"] = "mistake",
        ["wroids"] = "words",
        ["setgigna"] = "settings",
        ["coplme"] = "complete"
    };

    private static readonly Dictionary<string, string[]> ContextBoosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = ["want", "am", "will", "can", "need"],
        ["you"] = ["want", "are", "will", "can", "need"],
        ["to"] = ["the", "use", "make", "build", "fix"],
        ["not"] = ["working", "good", "fast"],
        ["use"] = ["the", "python", "onnx", "tensorflow"],
        ["small"] = ["library", "model"],
        ["fix"] = ["the", "word", "words"],
        ["make"] = ["it", "this", "fast", "smooth"]
    };

    public static void WarmUp()
    {
        _ = Index.Value;
    }

    public CorrectionResult? Correct(CorrectionRequest request, CorrectionSettings settings)
    {
        var original = request.Word.Trim();
        if (!CanConsider(original, settings))
        {
            return null;
        }

        if (IsProtected(original, settings) || IsUnsafeToken(original))
        {
            return null;
        }

        if (settings.LearnedCorrections.TryGetValue(original, out var learnedReplacement) &&
            !IsRejected(original, learnedReplacement, settings))
        {
            return BuildResult(original, learnedReplacement, 0.985, "learned correction", "personal-memory");
        }

        if (settings.CustomCorrections.TryGetValue(original, out var customReplacement) ||
            BuiltInCorrections.TryGetValue(original, out customReplacement))
        {
            if (IsRejected(original, customReplacement, settings))
            {
                return null;
            }

            return BuildResult(original, customReplacement, 0.995, "built-in correction", "built-in");
        }

        var lower = original.ToLowerInvariant();
        if (Index.Value.ContainsWord(lower) || IsLearnedWord(lower, settings))
        {
            return null;
        }

        var suggestion = Lookup(lower, request.PreviousWords, settings);
        var split = TrySplitMergedWord(lower, settings);
        if (split is not null && (suggestion is null || suggestion.Value.Confidence < 0.94))
        {
            return BuildResult(original, split, 0.965, "merged words", "split-words");
        }

        if (suggestion is null)
        {
            return null;
        }

        var confidence = suggestion.Value.Confidence;
        if (confidence < settings.ConfidenceThreshold)
        {
            return null;
        }

        if (IsRejected(original, suggestion.Value.Word, settings))
        {
            return null;
        }

        return BuildResult(original, suggestion.Value.Word, confidence, "symspell", "local-index");
    }

    public ValueTask<CorrectionResult?> CorrectAsync(
        CorrectionRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Correct(request, settings));
    }

    public IReadOnlyList<WordSuggestion> Suggest(
        string wordPrefix,
        IReadOnlyList<string> previousWords,
        CorrectionSettings settings,
        int limit = 5)
    {
        var word = wordPrefix.Trim().ToLowerInvariant();
        if (word.Length < 2 || word.Any(c => !char.IsLetter(c) && c != '\''))
        {
            return Array.Empty<WordSuggestion>();
        }

        var suggestions = new Dictionary<string, WordSuggestion>(StringComparer.OrdinalIgnoreCase);

        foreach (var (typo, replacement) in settings.CustomCorrections.Concat(BuiltInCorrections))
        {
            if (typo.StartsWith(word, StringComparison.OrdinalIgnoreCase) ||
                replacement.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                suggestions[replacement] = new WordSuggestion(replacement, 0.99, "custom");
            }
        }

        if (!Index.Value.ContainsWord(word) && !IsLearnedWord(word, settings))
        {
            var correction = Lookup(word, previousWords, settings);
            if (correction is not null)
            {
                suggestions[correction.Value.Word] = new WordSuggestion(
                    correction.Value.Word,
                    correction.Value.Confidence,
                    "correction");
            }
        }

        foreach (var suggestion in Index.Value.PrefixMatches(word, limit * 4))
        {
            suggestions.TryAdd(suggestion.Word, new WordSuggestion(
                suggestion.Word,
                0.72 + Math.Min(0.2, Math.Log10(Math.Max(10, suggestion.Frequency)) / 60),
                "dictionary"));
        }

        if (settings.EnableUserLearning)
        {
            foreach (var (learned, count) in settings.LearnedWordFrequencies)
            {
                if (count >= settings.LearnWordAfterCount &&
                    (learned.StartsWith(word, StringComparison.OrdinalIgnoreCase) ||
                     DamerauLevenshteinWithin(word, learned, 1) <= 1))
                {
                    suggestions[learned] = new WordSuggestion(
                        learned,
                        Math.Min(0.98, 0.80 + count * 0.025),
                        "personal");
                }
            }
        }

        return suggestions.Values
            .Where(s => !string.Equals(s.Text, wordPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Confidence)
            .ThenBy(s => s.Text.Length)
            .Take(limit)
            .ToArray();
    }

    private static bool CanConsider(string word, CorrectionSettings settings)
    {
        return word.Length >= 2 &&
               word.Length <= 32 &&
               !settings.IgnoredWords.Contains(word) &&
               word.All(c => char.IsLetter(c) || c == '\'');
    }

    private static bool IsProtected(string word, CorrectionSettings settings)
    {
        return settings.ProtectedVocabulary.Contains(word) ||
               settings.LearnedWordFrequencies.TryGetValue(word, out var count) && count >= settings.LearnWordAfterCount;
    }

    private static bool IsRejected(string original, string replacement, CorrectionSettings settings)
    {
        return settings.RejectedCorrections.TryGetValue($"{original.ToLowerInvariant()}->{replacement.ToLowerInvariant()}", out var count) &&
               count >= 1;
    }

    private static bool IsUnsafeToken(string word)
    {
        return word.Contains("://", StringComparison.Ordinal) ||
               word.Contains('@', StringComparison.Ordinal) ||
               word.Contains('\\', StringComparison.Ordinal) ||
               word.Contains('/', StringComparison.Ordinal) ||
               word.Contains('_', StringComparison.Ordinal) ||
               word.Contains('#', StringComparison.Ordinal) ||
               word.Contains('$', StringComparison.Ordinal) ||
               word.Any(char.IsDigit) ||
               word.Skip(1).Any(char.IsUpper);
    }

    private static string? TrySplitMergedWord(string input, CorrectionSettings settings)
    {
        if (input.Length < 6 || input.Length > 24)
        {
            return null;
        }

        SplitCandidate? best = null;
        for (var i = 2; i <= input.Length - 2; i++)
        {
            var left = BestStandaloneWord(input[..i], settings);
            var right = BestStandaloneWord(input[i..], settings);
            if (left is null || right is null)
            {
                continue;
            }

            if (!left.Value.WasCorrected && !right.Value.WasCorrected)
            {
                continue;
            }

            var score = left.Value.Confidence + right.Value.Confidence;
            if (best is null || score > best.Value.Score)
            {
                best = new SplitCandidate(left.Value.Word, right.Value.Word, score);
            }
        }

        return best is not null && best.Value.Score >= 1.86
            ? $"{best.Value.Left} {best.Value.Right}"
            : null;
    }

    private static (string Word, double Confidence, bool WasCorrected)? BestStandaloneWord(string token, CorrectionSettings settings)
    {
        if (Index.Value.ContainsWord(token))
        {
            return (token, 0.98, false);
        }

        if (settings.CustomCorrections.TryGetValue(token, out var custom) ||
            BuiltInCorrections.TryGetValue(token, out custom))
        {
            return (custom, 0.99, true);
        }

        var suggestion = Lookup(token, Array.Empty<string>(), settings);
        return suggestion is not null && suggestion.Value.Confidence >= 0.90
            ? (suggestion.Value.Word, suggestion.Value.Confidence, true)
            : null;
    }

    private static bool IsLearnedWord(string word, CorrectionSettings settings)
    {
        return settings.EnableUserLearning &&
               settings.LearnedWordFrequencies.TryGetValue(word, out var count) &&
               count >= settings.LearnWordAfterCount;
    }

    private static (string Word, double Confidence)? Lookup(
        string input,
        IReadOnlyList<string> previousWords,
        CorrectionSettings settings)
    {
        var index = Index.Value;
        var deletes = SymSpellIndex.GenerateDeletes(input, MaxEditDistance, PrefixLength);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var delete in deletes)
        {
            foreach (var candidate in index.GetCandidates(delete))
            {
                candidates.Add(candidate);
            }
        }

        AddLearnedCandidates(input, settings, candidates);
        AddRepeatedCharacterCandidates(input, candidates);
        AddScrambleCandidates(input, candidates);

        if (candidates.Count == 0)
        {
            return null;
        }

        CandidateScore? best = null;
        CandidateScore? second = null;
        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.Length - input.Length) > MaxEditDistance)
            {
                continue;
            }

            var isScramble = IsLooseScramble(input, candidate);
            var distance = DamerauLevenshteinWithin(input, candidate, MaxEditDistance);
            if ((distance > MaxEditDistance && !isScramble) || IsRiskyShortCorrection(input, candidate, distance))
            {
                continue;
            }

            var frequency = GetBlendedFrequency(candidate, settings);
            var effectiveDistance = isScramble ? Math.Min(distance, MaxEditDistance) : distance;
            var score = Score(input, candidate, effectiveDistance, frequency, previousWords, settings);
            var item = new CandidateScore(candidate, effectiveDistance, frequency, score);
            if (best is null || item.Score > best.Value.Score)
            {
                second = best;
                best = item;
            }
            else if (second is null || item.Score > second.Value.Score)
            {
                second = item;
            }
        }

        if (best is null)
        {
            return null;
        }

        var confidence = Confidence(input, best.Value, second);
        return (best.Value.Word, confidence);
    }

    private static void AddLearnedCandidates(string input, CorrectionSettings settings, HashSet<string> candidates)
    {
        if (!settings.EnableUserLearning)
        {
            return;
        }

        foreach (var (word, count) in settings.LearnedWordFrequencies)
        {
            if (count < settings.LearnWordAfterCount || Math.Abs(word.Length - input.Length) > MaxEditDistance)
            {
                continue;
            }

            if (DamerauLevenshteinWithin(input, word, MaxEditDistance) <= MaxEditDistance ||
                IsLooseScramble(input, word))
            {
                candidates.Add(word);
            }
        }
    }

    private static void AddScrambleCandidates(string input, HashSet<string> candidates)
    {
        if (input.Length is < 5 or > 10)
        {
            return;
        }

        foreach (var word in Index.Value.WordsBySignature(Signature(input)))
        {
            candidates.Add(word);
        }
    }

    private static void AddRepeatedCharacterCandidates(string input, HashSet<string> candidates)
    {
        if (input.Length < 4)
        {
            return;
        }

        foreach (var variant in RepeatedCharacterVariants(input))
        {
            if (Index.Value.ContainsWord(variant))
            {
                candidates.Add(variant);
            }

            foreach (var delete in SymSpellIndex.GenerateDeletes(variant, 1, PrefixLength))
            {
                foreach (var candidate in Index.Value.GetCandidates(delete))
                {
                    if (Math.Abs(candidate.Length - variant.Length) <= 1)
                    {
                        candidates.Add(candidate);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> RepeatedCharacterVariants(string input)
    {
        for (var i = 1; i < input.Length; i++)
        {
            if (input[i] == input[i - 1])
            {
                yield return input.Remove(i, 1);
            }
        }
    }

    private static bool IsRiskyShortCorrection(string input, string candidate, int distance)
    {
        return input.Length <= 4 && distance > 1 && !BuiltInCorrections.ContainsKey(input) &&
               !HasKeyboardNeighborPattern(input, candidate);
    }

    private static double Score(
        string input,
        string candidate,
        int distance,
        long frequency,
        IReadOnlyList<string> previousWords,
        CorrectionSettings settings)
    {
        var score = Math.Log10(Math.Max(10, frequency)) * 12;
        score -= distance * 18;
        score -= Math.Abs(candidate.Length - input.Length) * 2.5;

        if (IsTransposition(input, candidate))
        {
            score += 8;
        }

        if (HasKeyboardNeighborPattern(input, candidate))
        {
            score += 5;
        }

        if (IsLooseScramble(input, candidate))
        {
            score += 10;
        }

        if (settings.EnableUserLearning &&
            settings.LearnedWordFrequencies.TryGetValue(candidate, out var learnedCount) &&
            learnedCount >= settings.LearnWordAfterCount)
        {
            score += Math.Min(36, 18 + learnedCount * 2.5);
        }

        var previous = previousWords.LastOrDefault();
        if (previous is not null &&
            ContextBoosts.TryGetValue(previous, out var boosted) &&
            boosted.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            score += 12;
        }

        return score;
    }

    private static long GetBlendedFrequency(string candidate, CorrectionSettings settings)
    {
        var dictionaryFrequency = Index.Value.GetFrequency(candidate);
        if (!settings.EnableUserLearning ||
            !settings.LearnedWordFrequencies.TryGetValue(candidate, out var learnedCount) ||
            learnedCount < settings.LearnWordAfterCount)
        {
            return dictionaryFrequency;
        }

        var learnedFrequency = 30_000_000L + learnedCount * 8_000_000L;
        return Math.Max(dictionaryFrequency, learnedFrequency);
    }

    private static double Confidence(string input, CandidateScore best, CandidateScore? second)
    {
        var confidence = best.Distance switch
        {
            0 => 1.0,
            1 => 0.955,
            _ => input.Length >= 7 ? 0.93 : 0.88
        };

        if (IsTransposition(input, best.Word))
        {
            confidence += 0.025;
        }

        if (HasKeyboardNeighborPattern(input, best.Word))
        {
            confidence += 0.015;
        }

        if (IsLooseScramble(input, best.Word))
        {
            confidence += 0.03;
        }

        if (second is not null && best.Score - second.Value.Score < 8)
        {
            confidence -= 0.08;
        }

        return Math.Clamp(confidence, 0, 0.995);
    }

    private static CorrectionResult? BuildResult(string original, string replacement, double confidence, string reason, string source)
    {
        if (string.IsNullOrWhiteSpace(replacement))
        {
            return null;
        }

        var cased = ApplyCasing(original, replacement);
        return string.Equals(original, cased, StringComparison.Ordinal)
            ? null
            : new CorrectionResult(original, cased, confidence, reason, source);
    }

    private static string ApplyCasing(string original, string replacement)
    {
        if (original.All(char.IsUpper))
        {
            return replacement.ToUpperInvariant();
        }

        if (char.IsUpper(original[0]))
        {
            return char.ToUpper(replacement[0], CultureInfo.CurrentCulture) + replacement[1..];
        }

        return replacement;
    }

    private static bool IsTransposition(string input, string candidate)
    {
        if (input.Length != candidate.Length)
        {
            return false;
        }

        for (var i = 0; i < input.Length - 1; i++)
        {
            if (input[i] == candidate[i])
            {
                continue;
            }

            var chars = input.ToCharArray();
            (chars[i], chars[i + 1]) = (chars[i + 1], chars[i]);
            return string.Equals(new string(chars), candidate, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsLooseScramble(string input, string candidate)
    {
        if (input.Length != candidate.Length || input.Length < 5)
        {
            return false;
        }

        if (input[0] != candidate[0])
        {
            return false;
        }

        var inputCounts = CharacterCounts(input);
        var candidateCounts = CharacterCounts(candidate);
        if (!inputCounts.SequenceEqual(candidateCounts))
        {
            return false;
        }

        var fixedPositions = 0;
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == candidate[i])
            {
                fixedPositions++;
            }
        }

        return fixedPositions >= 1;
    }

    private static int[] CharacterCounts(string word)
    {
        var counts = new int[26];
        foreach (var c in word)
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z')
            {
                counts[lower - 'a']++;
            }
        }

        return counts;
    }

    private static string Signature(string word)
    {
        var chars = word.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray();
        Array.Sort(chars);
        return new string(chars);
    }

    private static bool HasKeyboardNeighborPattern(string input, string candidate)
    {
        if (input.Length != candidate.Length)
        {
            return false;
        }

        var mismatches = 0;
        for (var i = 0; i < input.Length; i++)
        {
            if (char.ToLowerInvariant(input[i]) == char.ToLowerInvariant(candidate[i]))
            {
                continue;
            }

            mismatches++;
            if (mismatches > 1 || !KeyboardNeighbors.AreNeighbors(input[i], candidate[i]))
            {
                return false;
            }
        }

        return mismatches == 1;
    }

    private static int DamerauLevenshteinWithin(string source, string target, int limit)
    {
        if (Math.Abs(source.Length - target.Length) > limit)
        {
            return limit + 1;
        }

        var previousPrevious = new int[target.Length + 1];
        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];

            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                var value = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                if (i > 1 &&
                    j > 1 &&
                    source[i - 1] == target[j - 2] &&
                    source[i - 2] == target[j - 1])
                {
                    value = Math.Min(value, previousPrevious[j - 2] + 1);
                }

                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > limit)
            {
                return limit + 1;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[target.Length];
    }

    private readonly record struct CandidateScore(string Word, int Distance, long Frequency, double Score);

    private readonly record struct SplitCandidate(string Left, string Right, double Score);

    private sealed class SymSpellIndex
    {
        private readonly IReadOnlyDictionary<string, long> _frequencies;
        private readonly Dictionary<string, string[]> _deleteIndex;
        private readonly Dictionary<string, string[]> _wordsBySignature;
        private readonly Dictionary<string, string[]> _prefixIndex;

        private SymSpellIndex(
            IReadOnlyDictionary<string, long> frequencies,
            Dictionary<string, string[]> deleteIndex,
            Dictionary<string, string[]> wordsBySignature,
            Dictionary<string, string[]> prefixIndex)
        {
            _frequencies = frequencies;
            _deleteIndex = deleteIndex;
            _wordsBySignature = wordsBySignature;
            _prefixIndex = prefixIndex;
        }

        public static SymSpellIndex Build(IReadOnlyDictionary<string, long> frequencies)
        {
            var mutable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in frequencies.Keys)
            {
                foreach (var delete in GenerateDeletes(word, MaxEditDistance, PrefixLength))
                {
                    if (!mutable.TryGetValue(delete, out var entries))
                    {
                        entries = [];
                        mutable[delete] = entries;
                    }

                    entries.Add(word);
                }
            }

            var compact = mutable.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
            var wordsBySignature = frequencies.Keys
                .Where(word => word.Length is >= 5 and <= 10)
                .GroupBy(Signature)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var prefixIndex = BuildPrefixIndex(frequencies);
            return new SymSpellIndex(frequencies, compact, wordsBySignature, prefixIndex);
        }

        public bool ContainsWord(string word)
        {
            return _frequencies.ContainsKey(word);
        }

        public long GetFrequency(string word)
        {
            return _frequencies.TryGetValue(word, out var frequency) ? frequency : 1;
        }

        public IReadOnlyList<string> GetCandidates(string delete)
        {
            return _deleteIndex.TryGetValue(delete, out var candidates) ? candidates : Array.Empty<string>();
        }

        public IReadOnlyList<string> WordsBySignature(string signature)
        {
            return _wordsBySignature.TryGetValue(signature, out var words) ? words : Array.Empty<string>();
        }

        public IReadOnlyList<(string Word, long Frequency)> PrefixMatches(string prefix, int limit)
        {
            var key = prefix.Length > 4 ? prefix[..4] : prefix;
            if (!_prefixIndex.TryGetValue(key, out var words))
            {
                return Array.Empty<(string Word, long Frequency)>();
            }

            return words
                .Where(word => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(word => (word, GetFrequency(word)))
                .OrderByDescending(pair => pair.Item2)
                .Take(limit)
                .ToArray();
        }

        private static Dictionary<string, string[]> BuildPrefixIndex(IReadOnlyDictionary<string, long> frequencies)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in frequencies.Keys.Where(word => word.Length >= 2))
            {
                var max = Math.Min(4, word.Length);
                for (var length = 2; length <= max; length++)
                {
                    var prefix = word[..length];
                    if (!index.TryGetValue(prefix, out var words))
                    {
                        words = [];
                        index[prefix] = words;
                    }

                    words.Add(word);
                }
            }

            return index.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderByDescending(word => frequencies[word])
                    .Take(250)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }

        public static HashSet<string> GenerateDeletes(string word, int maxDistance, int prefixLength)
        {
            var deletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string Word, int Distance)>();
            var prefix = word.Length > prefixLength ? word[..prefixLength] : word;
            deletes.Add(prefix);
            queue.Enqueue((prefix, 0));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Distance >= maxDistance || current.Word.Length == 0)
                {
                    continue;
                }

                for (var i = 0; i < current.Word.Length; i++)
                {
                    var delete = current.Word.Remove(i, 1);
                    if (deletes.Add(delete))
                    {
                        queue.Enqueue((delete, current.Distance + 1));
                    }
                }
            }

            return deletes;
        }
    }

    private static class KeyboardNeighbors
    {
        private static readonly Dictionary<char, string> Neighbors = new()
        {
            ['q'] = "wa",
            ['w'] = "qase",
            ['e'] = "wsdr",
            ['r'] = "edft",
            ['t'] = "rfgy",
            ['y'] = "tghu",
            ['u'] = "yhji",
            ['i'] = "ujko",
            ['o'] = "iklp",
            ['p'] = "ol",
            ['a'] = "qwsz",
            ['s'] = "awedxz",
            ['d'] = "serfcx",
            ['f'] = "drtgvc",
            ['g'] = "ftyhbv",
            ['h'] = "gyujnb",
            ['j'] = "huikmn",
            ['k'] = "jiolm",
            ['l'] = "kop",
            ['z'] = "asx",
            ['x'] = "zsdc",
            ['c'] = "xdfv",
            ['v'] = "cfgb",
            ['b'] = "vghn",
            ['n'] = "bhjm",
            ['m'] = "njk"
        };

        public static bool AreNeighbors(char left, char right)
        {
            var a = char.ToLowerInvariant(left);
            var b = char.ToLowerInvariant(right);
            return Neighbors.TryGetValue(a, out var neighbors) && neighbors.Contains(b);
        }
    }
}
