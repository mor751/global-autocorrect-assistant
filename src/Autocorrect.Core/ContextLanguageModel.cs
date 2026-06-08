namespace Autocorrect.Core;

public sealed class ContextLanguageModel
{
    public static ContextLanguageModel Default { get; } = new(EnglishContextTable.Build());

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> _bigrams;

    public ContextLanguageModel(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> bigrams)
    {
        _bigrams = bigrams;
    }

    // Returns 0..1 describing how strongly a candidate word tends to follow the preceding words.
    public double Affinity(IReadOnlyList<string> previousWords, string candidate, CorrectionSettings settings)
    {
        if (previousWords.Count == 0 || string.IsNullOrEmpty(candidate))
        {
            return 0;
        }

        var previous = previousWords[^1].ToLowerInvariant();
        var next = candidate.ToLowerInvariant();
        var affinity = CuratedAffinity(previous, next);

        if (settings.EnableUserLearning &&
            settings.LearnedBigrams.TryGetValue(BigramKey(previous, next), out var learnedCount) &&
            learnedCount > 0)
        {
            affinity = Math.Max(affinity, Math.Min(1.0, 0.55 + learnedCount * 0.09));
        }

        return affinity;
    }

    // Looks up the curated transition weight for a known word pair.
    private double CuratedAffinity(string previous, string next)
    {
        return _bigrams.TryGetValue(previous, out var transitions) &&
               transitions.TryGetValue(next, out var weight)
            ? weight
            : 0;
    }

    // Builds the flat "previous next" key used to store and look up personal bigrams.
    public static string BigramKey(string previous, string next)
    {
        return previous.ToLowerInvariant() + " " + next.ToLowerInvariant();
    }
}

internal static class EnglishContextTable
{
    // Curated, graded English word transitions used to rank correction candidates by context.
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Build()
    {
        var table = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        Add(table, "i", ("am", 0.9), ("want", 0.85), ("need", 0.85), ("will", 0.8), ("can", 0.8), ("have", 0.8), ("think", 0.75), ("would", 0.72), ("like", 0.7), ("was", 0.75), ("don't", 0.72), ("just", 0.65));
        Add(table, "you", ("are", 0.9), ("can", 0.85), ("want", 0.8), ("need", 0.8), ("will", 0.78), ("have", 0.78), ("should", 0.7), ("know", 0.7));
        Add(table, "we", ("are", 0.88), ("can", 0.85), ("need", 0.82), ("will", 0.8), ("have", 0.78), ("should", 0.74), ("want", 0.74));
        Add(table, "they", ("are", 0.88), ("will", 0.8), ("can", 0.78), ("have", 0.78), ("want", 0.72), ("need", 0.72));
        Add(table, "he", ("is", 0.9), ("was", 0.85), ("has", 0.8), ("will", 0.75), ("can", 0.72));
        Add(table, "she", ("is", 0.9), ("was", 0.85), ("has", 0.8), ("will", 0.75), ("can", 0.72));
        Add(table, "it", ("is", 0.9), ("was", 0.82), ("will", 0.78), ("can", 0.72), ("works", 0.7), ("should", 0.68));
        Add(table, "to", ("the", 0.9), ("be", 0.85), ("use", 0.8), ("make", 0.78), ("build", 0.75), ("fix", 0.75), ("do", 0.74), ("get", 0.72), ("run", 0.7), ("create", 0.7));
        Add(table, "the", ("same", 0.78), ("best", 0.76), ("first", 0.74), ("code", 0.72), ("model", 0.72), ("prompt", 0.72), ("text", 0.72), ("file", 0.7), ("user", 0.7), ("image", 0.7), ("video", 0.7), ("difference", 0.7));
        Add(table, "a", ("lot", 0.78), ("new", 0.76), ("small", 0.74), ("simple", 0.72), ("good", 0.72), ("video", 0.7), ("prompt", 0.7), ("model", 0.7), ("file", 0.68));
        Add(table, "of", ("the", 0.92), ("a", 0.7), ("code", 0.68), ("text", 0.66), ("tokens", 0.66));
        Add(table, "in", ("the", 0.9), ("a", 0.72), ("this", 0.7), ("code", 0.66), ("python", 0.64));
        Add(table, "on", ("the", 0.9), ("a", 0.7), ("windows", 0.7), ("this", 0.68));
        Add(table, "for", ("the", 0.85), ("this", 0.74), ("a", 0.72), ("each", 0.7), ("example", 0.68));
        Add(table, "with", ("the", 0.85), ("a", 0.72), ("this", 0.7), ("python", 0.66));
        Add(table, "and", ("the", 0.78), ("then", 0.74), ("not", 0.7), ("run", 0.66), ("make", 0.66));
        Add(table, "not", ("working", 0.88), ("good", 0.78), ("sure", 0.76), ("fast", 0.72), ("correct", 0.72), ("ready", 0.7));
        Add(table, "is", ("not", 0.82), ("the", 0.74), ("a", 0.72), ("working", 0.72), ("good", 0.7));
        Add(table, "are", ("not", 0.8), ("the", 0.72), ("you", 0.7), ("ready", 0.68));
        Add(table, "this", ("is", 0.88), ("code", 0.72), ("model", 0.7), ("prompt", 0.7), ("file", 0.7), ("one", 0.68), ("project", 0.68));
        Add(table, "that", ("is", 0.86), ("the", 0.72), ("works", 0.7), ("would", 0.68));
        Add(table, "make", ("it", 0.85), ("the", 0.78), ("this", 0.74), ("sure", 0.74), ("a", 0.7), ("them", 0.68));
        Add(table, "use", ("the", 0.85), ("a", 0.74), ("python", 0.72), ("this", 0.7), ("onnx", 0.66), ("tensorflow", 0.64));
        Add(table, "fix", ("the", 0.85), ("this", 0.76), ("it", 0.74), ("typos", 0.72), ("that", 0.7));
        Add(table, "want", ("to", 0.92), ("a", 0.7), ("the", 0.66), ("it", 0.64));
        Add(table, "need", ("to", 0.9), ("a", 0.72), ("the", 0.66), ("it", 0.62));
        Add(table, "will", ("be", 0.82), ("not", 0.72), ("make", 0.68), ("run", 0.66), ("work", 0.66));
        Add(table, "can", ("be", 0.78), ("not", 0.72), ("you", 0.72), ("make", 0.68), ("use", 0.68), ("run", 0.66));
        Add(table, "should", ("be", 0.82), ("not", 0.74), ("work", 0.7), ("use", 0.68));
        Add(table, "very", ("good", 0.78), ("fast", 0.74), ("important", 0.74), ("small", 0.7), ("simple", 0.7));
        Add(table, "really", ("good", 0.76), ("fast", 0.72), ("important", 0.72), ("want", 0.68));
        Add(table, "so", ("the", 0.66), ("it", 0.64), ("that", 0.66), ("i", 0.62));
        Add(table, "please", ("make", 0.74), ("fix", 0.74), ("use", 0.7), ("add", 0.7));
        Add(table, "small", ("model", 0.8), ("library", 0.76), ("change", 0.72), ("file", 0.7));
        Add(table, "video", ("prompt", 0.78), ("generation", 0.76), ("model", 0.7));
        Add(table, "coding", ("prompt", 0.78), ("model", 0.7), ("task", 0.68));
        Add(table, "open", ("source", 0.82), ("the", 0.68), ("a", 0.64));
        Add(table, "machine", ("learning", 0.9));
        Add(table, "neural", ("network", 0.9));
        Add(table, "read", ("this", 0.78), ("the", 0.76), ("it", 0.66));
        Add(table, "see", ("the", 0.78), ("this", 0.72), ("it", 0.68), ("difference", 0.66));
        Add(table, "auto", ("fix", 0.78), ("correct", 0.76));
        Add(table, "apply", ("the", 0.78), ("onnx", 0.66), ("a", 0.66), ("this", 0.64));
        Add(table, "supposed", ("to", 0.94));
        Add(table, "all", ("the", 0.86), ("of", 0.7), ("words", 0.66));
        Add(table, "what", ("you", 0.78), ("the", 0.7), ("is", 0.7), ("do", 0.66));
        Add(table, "do", ("you", 0.78), ("this", 0.7), ("the", 0.68), ("it", 0.66));
        Add(table, "smartest", ("engine", 0.7), ("model", 0.66), ("way", 0.66));

        return table;
    }

    // Stores one previous word with its weighted set of likely next words.
    private static void Add(
        Dictionary<string, IReadOnlyDictionary<string, double>> table,
        string previous,
        params (string Next, double Weight)[] transitions)
    {
        table[previous] = transitions.ToDictionary(
            transition => transition.Next,
            transition => transition.Weight,
            StringComparer.OrdinalIgnoreCase);
    }
}
