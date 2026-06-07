using Autocorrect.App;
using Autocorrect.Core;

var tests = new (string Name, Action Run)[]
{
    ("custom dictionary fixes component typo", CustomDictionaryFixesComponentTypo),
    ("symspell fixes common non-config typo", SymSpellFixesCommonNonConfigTypo),
    ("built-in fixes recent user typo examples", BuiltInFixesRecentUserTypoExamples),
    ("fixes repeated letter typing mistakes", FixesRepeatedLetterTypingMistakes),
    ("fixes short transposition words", FixesShortTranspositionWords),
    ("splits merged words", SplitsMergedWords),
    ("scrambled known word fixes to python", ScrambledKnownWordFixesToPython),
    ("learned personal word wins ranking", LearnedPersonalWordWinsRanking),
    ("suggestions include correction and learned words", SuggestionsIncludeCorrectionAndLearnedWords),
    ("protected vocabulary is not corrected", ProtectedVocabularyIsNotCorrected),
    ("unsafe tokens are not corrected", UnsafeTokensAreNotCorrected),
    ("correct words are left alone", CorrectWordsAreLeftAlone),
    ("ignored words are left alone", IgnoredWordsAreLeftAlone),
    ("replacement preserves title casing", ReplacementPreservesTitleCasing),
    ("engine stays fast after warmup", EngineStaysFastAfterWarmup),
    ("controller replaces only after delimiter", ControllerReplacesAfterDelimiter),
    ("controller honors sensitive contexts", ControllerHonorsSensitiveContexts),
    ("controller skips unsafe browser auto replacement", ControllerSkipsUnsafeBrowserAutoReplacement),
    ("controller handles backspace in current word", ControllerHandlesBackspace),
    ("controller does nothing while disabled", ControllerDoesNothingWhileDisabled),
    ("controller skips late AI correction after new typing", ControllerSkipsLateCorrectionAfterNewTyping)
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

static void ControllerReplacesAfterDelimiter()
{
    var keyboard = new FakeKeyboardMonitor();
    var replacer = new FakeTextReplacer();
    using var controller = NewController(keyboard, replacer: replacer);

    controller.Start();
    keyboard.Type("compomment");
    Assert.Null(replacer.LastReplacement);

    keyboard.Delimit(' ');

    WaitUntil(() => replacer.LastReplacement is not null);
    Assert.Equal(("compomment", "component", ' '), replacer.LastReplacement!.Value);
}

static void ControllerHonorsSensitiveContexts()
{
    var keyboard = new FakeKeyboardMonitor();
    var replacer = new FakeTextReplacer();
    using var controller = NewController(
        keyboard,
        context: new FakeContextDetector(new AppContextSnapshot("chrome", "Login", "password", true, "password field")),
        replacer: replacer);

    controller.Start();
    keyboard.Type("compomment");
    keyboard.Delimit(' ');
    Thread.Sleep(80);

    Assert.Null(replacer.LastReplacement);
}

static void ControllerSkipsUnsafeBrowserAutoReplacement()
{
    var keyboard = new FakeKeyboardMonitor();
    var replacer = new FakeTextReplacer();
    using var controller = NewController(
        keyboard,
        context: new FakeContextDetector(new AppContextSnapshot(
            "chrome",
            "Flow - Google",
            "Chrome_WidgetWin_1",
            false,
            IsBrowser: true)),
        replacer: replacer);

    controller.Start();
    keyboard.Type("compomment");
    keyboard.Delimit(' ');
    Thread.Sleep(120);

    Assert.Null(replacer.LastReplacement);
}

static void ControllerHandlesBackspace()
{
    var keyboard = new FakeKeyboardMonitor();
    var replacer = new FakeTextReplacer();
    using var controller = NewController(keyboard, replacer: replacer);

    controller.Start();
    keyboard.Type("compommenx");
    keyboard.Backspace();
    keyboard.Type("t");
    keyboard.Delimit(' ');

    WaitUntil(() => replacer.LastReplacement is not null);
    Assert.Equal(("compomment", "component", ' '), replacer.LastReplacement!.Value);
}

static void ControllerDoesNothingWhileDisabled()
{
    var keyboard = new FakeKeyboardMonitor();
    var replacer = new FakeTextReplacer();
    using var controller = NewController(keyboard, new CorrectionSettings { Enabled = false }, replacer: replacer);

    controller.Start();
    keyboard.Type("compomment");
    keyboard.Delimit(' ');
    Thread.Sleep(80);

    Assert.Null(replacer.LastReplacement);
}

static void ControllerSkipsLateCorrectionAfterNewTyping()
{
    var keyboard = new FakeKeyboardMonitor();
    var replacer = new FakeTextReplacer();
    using var controller = new AutocorrectController(
        keyboard,
        new FakeContextDetector(new AppContextSnapshot("notepad", "Untitled", "Edit", false)),
        replacer,
        new SlowCorrectionEngine(),
        new CorrectionSettings { MaxCorrectionLatencyMs = 500 },
        new RecentCorrectionStore(),
        new RuntimeStatusStore());

    controller.Start();
    keyboard.Type("wrng");
    keyboard.Delimit(' ');
    keyboard.Type("next");
    Thread.Sleep(250);

    Assert.Null(replacer.LastReplacement);
}

static CorrectionResult? Correct(string word)
{
    return new LocalCorrectionEngine().Correct(
        new CorrectionRequest(word, Array.Empty<string>(), AppContextSnapshot.Unknown),
        new CorrectionSettings());
}

static AutocorrectController NewController(
    FakeKeyboardMonitor keyboard,
    CorrectionSettings? settings = null,
    ITextContextDetector? context = null,
    FakeTextReplacer? replacer = null)
{
    return new AutocorrectController(
        keyboard,
        context ?? new FakeContextDetector(new AppContextSnapshot("notepad", "Untitled", "Edit", false)),
        replacer ?? new FakeTextReplacer(),
        new LocalCorrectionEngine(),
        settings ?? new CorrectionSettings(),
        new RecentCorrectionStore(),
        new RuntimeStatusStore());
}

static void WaitUntil(Func<bool> condition)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (condition())
        {
            return;
        }

        Thread.Sleep(10);
    }

    throw new InvalidOperationException("Timed out waiting for async controller work.");
}

internal sealed class FakeKeyboardMonitor : IKeyboardMonitor
{
    public event EventHandler<TypedKeyEventArgs>? KeyTyped;

    public bool Started { get; private set; }

    public void Start()
    {
        Started = true;
    }

    public void Stop()
    {
        Started = false;
    }

    public void Dispose()
    {
        Stop();
    }

    public void Type(string text)
    {
        foreach (var c in text)
        {
            KeyTyped?.Invoke(this, new TypedKeyEventArgs(TypedKeyKind.Character, c));
        }
    }

    public void Delimit(char delimiter)
    {
        KeyTyped?.Invoke(this, new TypedKeyEventArgs(TypedKeyKind.Delimiter, delimiter));
    }

    public void Backspace()
    {
        KeyTyped?.Invoke(this, new TypedKeyEventArgs(TypedKeyKind.Backspace));
    }
}

internal sealed class FakeContextDetector(AppContextSnapshot context) : ITextContextDetector
{
    public AppContextSnapshot GetActiveContext(CorrectionSettings settings)
    {
        return context;
    }
}

internal sealed class FakeTextReplacer : ITextReplacer
{
    public (string Original, string Replacement, char Delimiter)? LastReplacement { get; private set; }

    public ReplacementResult ReplaceCompletedWord(string originalWord, string replacement, char delimiter)
    {
        LastReplacement = (originalWord, replacement, delimiter);
        return new ReplacementResult(true, "fake", null, originalWord, replacement);
    }

    public ReplacementResult ReplaceCurrentWord(string originalWord, string replacement)
    {
        LastReplacement = (originalWord, replacement, '\0');
        return new ReplacementResult(true, "fake", null, originalWord, replacement);
    }
}

internal sealed class SlowCorrectionEngine : IAsyncCorrectionEngine
{
    public async ValueTask<CorrectionResult?> CorrectAsync(
        CorrectionRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);
        return new CorrectionResult(request.Word, "wrong", 0.99, "slow fake");
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
