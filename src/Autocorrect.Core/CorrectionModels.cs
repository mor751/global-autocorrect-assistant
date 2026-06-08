namespace Autocorrect.Core;

public sealed record CorrectionRequest(
    string Word,
    IReadOnlyList<string> PreviousWords,
    AppContextSnapshot AppContext,
    char? NextDelimiter = null,
    string InputLanguage = "en");

public sealed record CorrectionResult(
    string Original,
    string Replacement,
    double Confidence,
    string Reason,
    string Source = "local",
    bool ShouldReplace = true)
{
    public bool HasChange => !string.Equals(Original, Replacement, StringComparison.Ordinal);
}

public sealed record WordSuggestion(string Text, double Confidence, string Reason);

public sealed record ReplacementResult(
    bool Success,
    string Method,
    string? Error,
    string Original,
    string Replacement);

public sealed record CorrectionHistoryEntry(
    string Original,
    string Replacement,
    DateTimeOffset Timestamp,
    string ProcessName,
    string WindowTitle,
    string ContextSnippetHash,
    double Confidence,
    string Source,
    string Method);

public enum AiRewriteAction
{
    FixTyposOnly,
    ImproveClarity,
    OptimizePrompt,
    CompressTokens,
    SmartOptimize,
    MakeProfessional,
    MakeDirect,
    TranslateHebrewEnglish,
    CursorCodingPrompt,
    VideoGenerationPrompt
}

public sealed record AiRewriteRequest(
    string Text,
    AiRewriteAction Action,
    AppContextSnapshot AppContext,
    string UserStyleProfile,
    string TargetTokenMode);

public sealed record AiRewriteResult(
    string RewrittenText,
    string ExplanationShort,
    int EstimatedTokenReductionPercent,
    double Confidence);

public interface ICorrectionEngine
{
    CorrectionResult? Correct(CorrectionRequest request, CorrectionSettings settings);
}

public interface IAsyncCorrectionEngine
{
    ValueTask<CorrectionResult?> CorrectAsync(
        CorrectionRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken);
}

public interface IWordSuggestionEngine
{
    IReadOnlyList<WordSuggestion> Suggest(
        string wordPrefix,
        IReadOnlyList<string> previousWords,
        CorrectionSettings settings,
        int limit = 5);
}

public interface ITextContextDetector
{
    AppContextSnapshot GetActiveContext(CorrectionSettings settings);
}

public interface ITextReplacer
{
    ReplacementResult ReplaceCompletedWord(string originalWord, string replacement, char delimiter);

    ReplacementResult ReplaceCurrentWord(string originalWord, string replacement);
}

public interface IWordLearningStore
{
    void RecordTypedWord(string word);

    void RecordWordPair(string previousWord, string word);

    void RecordAcceptedCorrection(string original, string replacement);

    void RecordRejectedCorrection(string original, string replacement);
}

public interface ICorrectionHistory
{
    void Record(CorrectionHistoryEntry entry);

    CorrectionHistoryEntry? Last();

    CorrectionHistoryEntry? PopLast();

    IReadOnlyList<CorrectionHistoryEntry> Snapshot(int limit);
}

public interface IAiRewriteService
{
    bool IsEnabled(CorrectionSettings settings);

    Task<AiRewriteResult?> RewriteAsync(
        AiRewriteRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken);
}

public interface IKeyboardMonitor : IDisposable
{
    event EventHandler<TypedKeyEventArgs>? KeyTyped;

    void Start();

    void Stop();
}

public enum TypedKeyKind
{
    Character,
    Delimiter,
    Backspace,
    NavigationUp,
    NavigationDown,
    AcceptSuggestion,
    Reset
}

public sealed class TypedKeyEventArgs : EventArgs
{
    public TypedKeyEventArgs(TypedKeyKind kind, char? character = null)
    {
        Kind = kind;
        Character = character;
    }

    public TypedKeyKind Kind { get; }

    public char? Character { get; }

    public bool Handled { get; set; }
}

public sealed class NullWordLearningStore : IWordLearningStore
{
    public static NullWordLearningStore Instance { get; } = new();

    private NullWordLearningStore()
    {
    }

    public void RecordTypedWord(string word)
    {
    }

    public void RecordWordPair(string previousWord, string word)
    {
    }

    public void RecordAcceptedCorrection(string original, string replacement)
    {
    }

    public void RecordRejectedCorrection(string original, string replacement)
    {
    }
}
