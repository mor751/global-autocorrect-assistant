namespace Autocorrect.Core;

public sealed record AppContextSnapshot(
    string ProcessName,
    string WindowTitle,
    string ControlName,
    bool IsSensitive,
    string? Reason = null,
    bool IsEditable = true,
    bool IsPasswordField = false,
    bool IsTerminal = false,
    bool IsCodeEditor = false,
    bool IsBrowser = false,
    bool IsLikelyPromptBox = false,
    bool IsAllowedForAutocorrect = true,
    bool IsAllowedForAiOverlay = true)
{
    public static AppContextSnapshot Unknown { get; } = new(
        ProcessName: string.Empty,
        WindowTitle: string.Empty,
        ControlName: string.Empty,
        IsSensitive: false);
}
