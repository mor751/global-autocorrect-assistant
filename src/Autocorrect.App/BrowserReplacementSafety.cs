using System.Windows.Automation;
using Autocorrect.Core;

namespace Autocorrect.App;

internal static class BrowserReplacementSafety
{
    public static bool CanReplaceCompletedWord(
        AppContextSnapshot context,
        string originalWord,
        string replacement,
        char delimiter,
        out string reason)
    {
        return CanReplace(context, originalWord, replacement, delimiter, includeDelimiter: true, out reason);
    }

    public static bool CanReplaceCurrentWord(
        AppContextSnapshot context,
        string originalWord,
        string replacement,
        out string reason)
    {
        return CanReplace(context, originalWord, replacement, delimiter: null, includeDelimiter: false, out reason);
    }

    private static bool CanReplace(
        AppContextSnapshot context,
        string originalWord,
        string replacement,
        char? delimiter,
        bool includeDelimiter,
        out string reason)
    {
        reason = string.Empty;
        if (!context.IsBrowser)
        {
            return true;
        }

        if (replacement.Any(char.IsWhiteSpace))
        {
            reason = "browser auto-replace skips multi-word replacements";
            return false;
        }

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null ||
                !focused.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ||
                pattern is not ValuePattern valuePattern ||
                valuePattern.Current.IsReadOnly)
            {
                reason = "browser editor does not expose a simple editable value";
                return false;
            }

            var value = valuePattern.Current.Value ?? string.Empty;
            var expectedSuffix = originalWord + (includeDelimiter ? delimiter!.Value : string.Empty);
            if (!value.EndsWith(expectedSuffix, StringComparison.Ordinal))
            {
                reason = "browser editor value does not end with the completed word";
                return false;
            }

            return true;
        }
        catch
        {
            reason = "browser editor safety check failed";
            return false;
        }
    }
}
