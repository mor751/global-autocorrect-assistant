using System.Reflection;

namespace Autocorrect.Core;

internal static class EnglishFrequencyDictionary
{
    private const string ResourceName = "Autocorrect.Core.Data.frequency_dictionary_en_82_765.txt";

    private static readonly Lazy<IReadOnlyDictionary<string, long>> Words = new(Load);

    public static IReadOnlyDictionary<string, long> All => Words.Value;

    private static IReadOnlyDictionary<string, long> Load()
    {
        var words = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            AddFallbackWords(words);
            return words;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !long.TryParse(parts[1], out var frequency))
            {
                continue;
            }

            var word = parts[0];
            if (IsDictionaryWord(word))
            {
                words[word] = frequency;
            }
        }

        AddProductWords(words);
        return words;
    }

    private static bool IsDictionaryWord(string word)
    {
        return word.Length is >= 2 and <= 24 &&
               word.All(c => char.IsLetter(c) || c == '\'') &&
               word.Any(char.IsLetter);
    }

    private static void AddProductWords(Dictionary<string, long> words)
    {
        Add(words, "autocorrect", 5_000_000);
        Add(words, "ollama", 2_000_000);
        Add(words, "onnx", 2_000_000);
        Add(words, "tensorflow", 2_000_000);
        Add(words, "symspell", 1_500_000);
        Add(words, "component", 15_000_000);
        Add(words, "configuration", 12_000_000);
        Add(words, "keyboard", 12_000_000);
        Add(words, "background", 10_000_000);
        Add(words, "docker", 7_000_000);
    }

    private static void AddFallbackWords(Dictionary<string, long> words)
    {
        foreach (var (word, frequency) in new (string Word, long Frequency)[]
        {
            ("the", 100_000_000),
            ("of", 90_000_000),
            ("and", 80_000_000),
            ("to", 70_000_000),
            ("in", 60_000_000),
            ("is", 50_000_000),
            ("you", 40_000_000),
            ("that", 30_000_000),
            ("computer", 10_000_000),
            ("component", 9_000_000),
            ("keyboard", 8_000_000),
            ("configuration", 7_000_000),
            ("correction", 6_000_000),
            ("background", 5_000_000),
            ("software", 4_000_000)
        })
        {
            Add(words, word, frequency);
        }
    }

    private static void Add(Dictionary<string, long> words, string word, long frequency)
    {
        words[word] = Math.Max(words.GetValueOrDefault(word), frequency);
    }
}
