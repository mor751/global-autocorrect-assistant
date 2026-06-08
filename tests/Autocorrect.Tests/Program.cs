using System;
using System.IO;
using System.Linq;
using System.Threading;
using Autocorrect.Core;
using Autocorrect.Core.Brain;

var tests = new (string Name, Action Run)[]
{
    ("brain indexer detects stack and ignores secrets", BrainIndexerDetectsStackAndIgnoresSecrets),
    ("brain retrieval finds login file for animation prompt", BrainRetrievalFindsLoginFile),
    ("prompt analyzer flags vague prompt", PromptAnalyzerFlagsVaguePrompt),
    ("smart rewriter works without ollama", SmartRewriterWorksWithoutOllama),
    ("secret scanner redacts keys", SecretScannerRedactsKeys),
    ("custom dictionary fixes component typo", CustomDictionaryFixesComponentTypo),
    ("symspell fixes common non-config typo", SymSpellFixesCommonNonConfigTypo),
    ("built-in fixes recent user typo examples", BuiltInFixesRecentUserTypoExamples),
    ("fixes repeated letter typing mistakes", FixesRepeatedLetterTypingMistakes),
    ("fixes short transposition words", FixesShortTranspositionWords),
    ("splits merged words", SplitsMergedWords),
    ("scrambled known word fixes to python", ScrambledKnownWordFixesToPython),
    ("fixes heavy jumbled words", FixesHeavyJumbledWords),
    ("learned personal word wins ranking", LearnedPersonalWordWinsRanking),
    ("context model scores known transitions", ContextModelScoresKnownTransitions),
    ("learned bigram raises context affinity", LearnedBigramRaisesContextAffinity),
    ("suggestions include correction and learned words", SuggestionsIncludeCorrectionAndLearnedWords),
    ("protected vocabulary is not corrected", ProtectedVocabularyIsNotCorrected),
    ("unsafe tokens are not corrected", UnsafeTokensAreNotCorrected),
    ("correct words are left alone", CorrectWordsAreLeftAlone),
    ("ignored words are left alone", IgnoredWordsAreLeftAlone),
    ("replacement preserves title casing", ReplacementPreservesTitleCasing),
    ("engine stays fast after warmup", EngineStaysFastAfterWarmup)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

if (failures > 0)
{
    Environment.ExitCode = 1;
}

static void CustomDictionaryFixesComponentTypo()
{
    var result = Correct("compomment");

    Assert.NotNull(result);
    Assert.Equal("component", result!.Replacement);
    Assert.True(result.Confidence >= 0.99, "Expected custom dictionary confidence.");
}

static void SymSpellFixesCommonNonConfigTypo()
{
    var result = Correct("acommodation");

    Assert.NotNull(result);
    Assert.Equal("accommodation", result!.Replacement);
    Assert.True(result.Reason.Contains("symspell", StringComparison.OrdinalIgnoreCase), "Expected indexed spell engine.");
}

static void BuiltInFixesRecentUserTypoExamples()
{
    var examples = new Dictionary<string, string>
    {
        ["dotn"] = "don't",
        ["pythoin"] = "python",
        ["tensorfloe"] = "tensorflow",
        ["soemthgin"] = "something",
        ["confgiartiuon"] = "configuration",
        ["nmsiektae"] = "mistake",
        ["wroids"] = "words",
        ["setgigna"] = "settings",
        ["coplme"] = "complete"
    };

    foreach (var (typo, expected) in examples)
    {
        var result = Correct(typo);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Replacement);
    }
}

static void FixesRepeatedLetterTypingMistakes()
{
    var examples = new Dictionary<string, string>
    {
        ["wrtiee"] = "write",
        ["writtee"] = "write",
        ["goood"] = "good"
    };

    foreach (var (typo, expected) in examples)
    {
        var result = Correct(typo);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Replacement);
    }
}

static void FixesShortTranspositionWords()
{
    var result = Correct("ti");

    Assert.NotNull(result);
    Assert.Equal("it", result!.Replacement);
}

static void SplitsMergedWords()
{
    var result = Correct("howthsi");

    Assert.NotNull(result);
    Assert.Equal("how this", result!.Replacement);
}

static void ScrambledKnownWordFixesToPython()
{
    var result = Correct("ptyonh");

    Assert.NotNull(result);
    Assert.Equal("python", result!.Replacement);
}

static void FixesHeavyJumbledWords()
{
    foreach (var (typo, expected) in new[] { ("usspeod", "supposed"), ("idffrence", "difference") })
    {
        var result = Correct(typo);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Replacement);
    }
}

static void LearnedPersonalWordWinsRanking()
{
    var settings = new CorrectionSettings
    {
        LearnWordAfterCount = 3,
        LearnedWordFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bro"] = 9
        }
    };
    var engine = new LocalCorrectionEngine();

    var learnedWord = engine.Correct(
        new CorrectionRequest("bro", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);
    var typo = engine.Correct(
        new CorrectionRequest("bor", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);

    Assert.Null(learnedWord);
    Assert.NotNull(typo);
    Assert.Equal("bro", typo!.Replacement);
}

static void SuggestionsIncludeCorrectionAndLearnedWords()
{
    var settings = new CorrectionSettings
    {
        LearnedWordFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bro"] = 5
        }
    };
    var engine = new LocalCorrectionEngine();

    var pythonSuggestions = engine.Suggest("ptyonh", Array.Empty<string>(), settings);
    var personalSuggestions = engine.Suggest("br", Array.Empty<string>(), settings);

    Assert.True(pythonSuggestions.Any(s => s.Text.Equals("python", StringComparison.OrdinalIgnoreCase)), "Expected python suggestion.");
    Assert.True(personalSuggestions.Any(s => s.Text.Equals("bro", StringComparison.OrdinalIgnoreCase)), "Expected learned bro suggestion.");
}

static void ProtectedVocabularyIsNotCorrected()
{
    var settings = new CorrectionSettings();
    settings.ProtectedVocabulary.Add("Promto");
    var result = new LocalCorrectionEngine().Correct(
        new CorrectionRequest("Promto", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);

    Assert.Null(result);
}

static void UnsafeTokensAreNotCorrected()
{
    Assert.Null(Correct("myFileName"));
    Assert.Null(Correct("abc123"));
}

static void CorrectWordsAreLeftAlone()
{
    Assert.Null(Correct("component"));
}

static void IgnoredWordsAreLeftAlone()
{
    var settings = new CorrectionSettings();
    settings.IgnoredWords.Add("compomment");

    var result = new LocalCorrectionEngine().Correct(
        new CorrectionRequest("compomment", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);

    Assert.Null(result);
}

static void ReplacementPreservesTitleCasing()
{
    var result = Correct("Compomment");

    Assert.NotNull(result);
    Assert.Equal("Component", result!.Replacement);
}

static void EngineStaysFastAfterWarmup()
{
    SymSpellCorrectionEngine.WarmUp();
    var engine = new LocalCorrectionEngine();
    var settings = new CorrectionSettings();
    var words = new[] { "acommodation", "recieve", "keybroad", "compomment", "pythoin", "soemthgin" };
    var started = DateTimeOffset.UtcNow;

    for (var i = 0; i < 500; i++)
    {
        var word = words[i % words.Length];
        _ = engine.Correct(new CorrectionRequest(word, Array.Empty<string>(), AppContextSnapshot.Unknown), settings);
    }

    var elapsed = DateTimeOffset.UtcNow - started;
    Assert.True(elapsed.TotalMilliseconds < 1000, $"Expected 500 warm lookups under 1000 ms, got {elapsed.TotalMilliseconds:0.0} ms.");
}

static void ContextModelScoresKnownTransitions()
{
    var model = ContextLanguageModel.Default;
    var settings = new CorrectionSettings();

    Assert.True(model.Affinity(new[] { "i" }, "want", settings) > 0.5, "Expected 'i want' to score highly.");
    Assert.True(model.Affinity(new[] { "qqqq" }, "want", settings) == 0, "Unknown previous word should score zero.");
    Assert.True(model.Affinity(Array.Empty<string>(), "want", settings) == 0, "No context should score zero.");
}

static void LearnedBigramRaisesContextAffinity()
{
    var model = ContextLanguageModel.Default;
    var settings = new CorrectionSettings();

    var before = model.Affinity(new[] { "video" }, "kling", settings);
    settings.LearnedBigrams[ContextLanguageModel.BigramKey("video", "kling")] = 4;
    var after = model.Affinity(new[] { "video" }, "kling", settings);

    Assert.True(before == 0, "Unknown personal pair should start at zero.");
    Assert.True(after > 0.8, "Learned personal pair should raise context affinity.");
}

static CorrectionResult? Correct(string word)
{
    return new LocalCorrectionEngine().Correct(
        new CorrectionRequest(word, Array.Empty<string>(), AppContextSnapshot.Unknown),
        new CorrectionSettings());
}

static string CreateSampleProject()
{
    var root = Path.Combine(Path.GetTempPath(), "brain-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(root, "src", "components"));
    Directory.CreateDirectory(Path.Combine(root, "src", "app"));

    File.WriteAllText(Path.Combine(root, "package.json"),
        "{\"name\":\"sample\",\"dependencies\":{\"react\":\"18.0.0\",\"tailwindcss\":\"3.4.0\",\"gsap\":\"3.12.0\"}}");
    File.WriteAllText(Path.Combine(root, "tailwind.config.js"), "module.exports = { content: [] };");
    File.WriteAllText(Path.Combine(root, "src", "components", "LoginForm.tsx"),
        "import gsap from 'gsap';\nexport function LoginForm() {\n  gsap.to('.btn', { opacity: 1 });\n  return null;\n}\n");
    File.WriteAllText(Path.Combine(root, "src", "app", "page.tsx"),
        "export default function Page() { return null; }\n");
    File.WriteAllText(Path.Combine(root, "src", "config.ts"),
        "export const token = \"sk-abcdef123456789\";\n");
    File.WriteAllText(Path.Combine(root, ".env"), "API_KEY=sk-supersecretvalue1234\n");
    return root;
}

static void BrainIndexerDetectsStackAndIgnoresSecrets()
{
    var root = CreateSampleProject();
    try
    {
        var brain = new ProjectIndexer().Index(root, new IndexOptions());

        Assert.Equal("React", brain.Stack.Framework);
        Assert.Equal("Tailwind CSS", brain.Stack.Styling);
        Assert.True(brain.Rules.Any(r => r.Contains("GSAP", StringComparison.OrdinalIgnoreCase)), "Expected GSAP rule.");
        Assert.True(brain.Files.All(f => !f.Path.EndsWith(".env", StringComparison.OrdinalIgnoreCase)), ".env must be ignored.");
        Assert.True(brain.Files.All(f => f.PreviewChunks.All(c => !c.Contains("abcdef123456789", StringComparison.Ordinal))), "Secrets must be redacted.");
    }
    finally
    {
        TryDelete(root);
    }
}

static void BrainRetrievalFindsLoginFile()
{
    var root = CreateSampleProject();
    var brainBase = Path.Combine(Path.GetTempPath(), "brain-store-" + Guid.NewGuid().ToString("N")[..8]);
    try
    {
        var brain = new ProjectIndexer().Index(root, new IndexOptions());
        var store = new FileVectorStore(brainBase, root, new KeywordEmbeddingProvider());
        store.AddDocumentsAsync(brain.Files.Select(f => new VectorDocument
        {
            Id = f.Path,
            Path = f.Path,
            Role = f.Role,
            Summary = f.Summary,
            Text = $"{f.Path} {f.Summary} {string.Join(' ', f.Symbols)}"
        }), CancellationToken.None).GetAwaiter().GetResult();

        var hits = store.SearchAsync("fix the login animation", 6, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(hits.Any(h => h.Document.Path.Contains("LoginForm", StringComparison.OrdinalIgnoreCase)), "Expected LoginForm in retrieval.");
    }
    finally
    {
        TryDelete(root);
        TryDelete(brainBase);
    }
}

static void PromptAnalyzerFlagsVaguePrompt()
{
    var analysis = new PromptAnalyzer().Analyze(
        "make this page better",
        null,
        Array.Empty<RetrievedFile>(),
        new UserPreferences());

    Assert.True(analysis.ShouldAskUserForMoreInfo || analysis.MissingContext.Count > 0, "Vague prompt should request more info.");
}

static void SmartRewriterWorksWithoutOllama()
{
    var root = CreateSampleProject();
    try
    {
        var brain = new ProjectIndexer().Index(root, new IndexOptions());
        var retrieved = brain.Files
            .Where(f => f.Path.Contains("LoginForm", StringComparison.OrdinalIgnoreCase))
            .Select(f => new RetrievedFile { File = f, Reason = "keyword match", Score = 1 })
            .ToList();
        var analysis = new PromptAnalyzer().Analyze("fix the login animation", brain, retrieved, new UserPreferences());

        var result = new SmartPromptRewriter(null)
            .BuildAsync("fix the login animation", analysis, brain, retrieved, new UserPreferences(), false, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.ImprovedPrompt.Contains("Task:", StringComparison.Ordinal), "Structured prompt expected.");
        Assert.True(result.ImprovedPrompt.Contains("Instructions:", StringComparison.Ordinal), "Instructions section expected.");
        Assert.False(result.UsedOllama);
    }
    finally
    {
        TryDelete(root);
    }
}

static void SecretScannerRedactsKeys()
{
    Assert.True(SecretScanner.Redact("API_KEY=sk-1234567890abcdef").Contains("[REDACTED]", StringComparison.Ordinal), "API key should be redacted.");
    Assert.True(SecretScanner.Redact("postgres://user:pass@host:5432/db").Contains("[REDACTED]", StringComparison.Ordinal), "Database URL should be redacted.");
}

static void TryDelete(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
    catch
    {
        // Temp cleanup is best-effort.
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
        }
    }

    public static void Null(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"Expected null, got {value}.");
        }
    }

    public static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected non-null value.");
        }
    }
}
