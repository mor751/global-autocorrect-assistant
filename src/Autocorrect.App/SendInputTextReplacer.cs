using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using Autocorrect.Core;
using Clipboard = System.Windows.Clipboard;

namespace Autocorrect.App;

public sealed class SendInputTextReplacer : ITextReplacer
{
    private readonly CorrectionSettings _settings;

    public SendInputTextReplacer(CorrectionSettings settings)
    {
        _settings = settings;
    }

    public ReplacementResult ReplaceCompletedWord(string originalWord, string replacement, char delimiter)
    {
        var text = replacement + delimiter;
        return ReplaceText(originalWord, text, includeDelimiter: true);
    }

    public ReplacementResult ReplaceCurrentWord(string originalWord, string replacement)
    {
        return ReplaceText(originalWord, replacement, includeDelimiter: false);
    }

    private ReplacementResult ReplaceText(string originalWord, string text, bool includeDelimiter)
    {
        if (TryReplaceWithUiAutomation(originalWord, text, includeDelimiter, out var automationError))
        {
            return new ReplacementResult(true, "ui-automation", null, originalWord, text);
        }

        if (IsForegroundBrowser())
        {
            return new ReplacementResult(
                false,
                "none",
                $"Browser editor replacement requires UI Automation; unsafe synthetic input skipped. UI Automation: {automationError}",
                originalWord,
                text);
        }

        if (TryReplaceWithUnicodeInput(originalWord, text, includeDelimiter, out var unicodeError))
        {
            return new ReplacementResult(true, "sendinput-unicode", null, originalWord, text);
        }

        var clipboardError = string.Empty;
        if (_settings.UseClipboardFallback && TryReplaceWithClipboardPaste(originalWord, text, includeDelimiter, out clipboardError))
        {
            return new ReplacementResult(true, "clipboard-paste", null, originalWord, text);
        }

        var fallbackText = _settings.UseClipboardFallback
            ? $"; clipboard fallback: {clipboardError}"
            : "; clipboard fallback disabled";
        var error = $"Could not replace text. UI Automation: {automationError}; Unicode input: {unicodeError}{fallbackText}";
        return new ReplacementResult(false, "none", error, originalWord, text);
    }

    private static bool IsForegroundBrowser()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            if (processId == 0)
            {
                return false;
            }

            var processName = Process.GetProcessById((int)processId).ProcessName;
            return processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                   processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                   processName.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
                   processName.Equals("brave", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReplaceWithUiAutomation(string originalWord, string text, bool includeDelimiter, out string error)
    {
        error = string.Empty;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null ||
                !focused.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ||
                pattern is not ValuePattern valuePattern ||
                valuePattern.Current.IsReadOnly)
            {
                error = "focused control does not expose editable ValuePattern";
                return false;
            }

            var value = valuePattern.Current.Value ?? string.Empty;
            var suffixLength = originalWord.Length + (includeDelimiter ? 1 : 0);
            var expectedSuffix = originalWord + (includeDelimiter ? text[^1] : string.Empty);
            if (value.Length < suffixLength ||
                !value.EndsWith(expectedSuffix, StringComparison.Ordinal))
            {
                error = "focused value does not end with completed word";
                return false;
            }

            var nextValue = value[..^suffixLength] + text;
            valuePattern.SetValue(nextValue);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReplaceWithUnicodeInput(string originalWord, string text, bool includeDelimiter, out string error)
    {
        var backspaceCount = originalWord.Length + (includeDelimiter ? 1 : 0);
        var inputs = new List<NativeMethods.Input>();

        for (var i = 0; i < backspaceCount; i++)
        {
            AddVirtualKey(inputs, NativeMethods.VK_BACK);
        }

        AddUnicodeText(inputs, text);
        return TrySendBatch(inputs, out error);
    }

    private static bool TryReplaceWithClipboardPaste(string originalWord, string text, bool includeDelimiter, out string error)
    {
        error = string.Empty;
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            var clipboardResult = dispatcher.Invoke(() =>
            {
                var ok = TrySetClipboardText(text, out var clipboardError);
                return (ok, clipboardError);
            });
            if (!clipboardResult.ok)
            {
                error = clipboardResult.clipboardError;
                return false;
            }

            var backspaces = BuildBackspaces(originalWord.Length + (includeDelimiter ? 1 : 0));
            if (!TrySendBatch(backspaces, out error))
            {
                return false;
            }

            var paste = new List<NativeMethods.Input>();
            AddKeyDown(paste, NativeMethods.VK_CONTROL);
            AddVirtualKey(paste, NativeMethods.VK_V);
            AddKeyUp(paste, NativeMethods.VK_CONTROL);
            return TrySendBatch(paste, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TrySetClipboardText(string text, out string error)
    {
        try
        {
            Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static List<NativeMethods.Input> BuildBackspaces(int count)
    {
        var inputs = new List<NativeMethods.Input>();
        for (var i = 0; i < count; i++)
        {
            AddVirtualKey(inputs, NativeMethods.VK_BACK);
        }

        return inputs;
    }

    private static bool TrySendBatch(List<NativeMethods.Input> inputs, out string error)
    {
        error = string.Empty;
        foreach (var chunk in inputs.Chunk(16))
        {
            var sent = NativeMethods.SendInput((uint)chunk.Length, chunk, Marshal.SizeOf<NativeMethods.Input>());
            if (sent != chunk.Length)
            {
                var lastError = Marshal.GetLastWin32Error();
                error = $"sent {sent}/{chunk.Length}, Win32Error={lastError}";
                return false;
            }
        }

        return true;
    }

    private static void AddVirtualKey(List<NativeMethods.Input> inputs, int virtualKey)
    {
        AddKeyDown(inputs, virtualKey);
        AddKeyUp(inputs, virtualKey);
    }

    private static void AddKeyDown(List<NativeMethods.Input> inputs, int virtualKey)
    {
        inputs.Add(new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Ki = new NativeMethods.KeybdInput { WVk = (ushort)virtualKey }
        });
    }

    private static void AddKeyUp(List<NativeMethods.Input> inputs, int virtualKey)
    {
        inputs.Add(new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Ki = new NativeMethods.KeybdInput { WVk = (ushort)virtualKey, DwFlags = NativeMethods.KEYEVENTF_KEYUP }
        });
    }

    private static void AddUnicodeText(List<NativeMethods.Input> inputs, string text)
    {
        foreach (var c in text)
        {
            AddUnicode(inputs, c);
        }
    }

    private static void AddUnicode(List<NativeMethods.Input> inputs, char c)
    {
        inputs.Add(new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Ki = new NativeMethods.KeybdInput
            {
                WScan = c,
                DwFlags = NativeMethods.KEYEVENTF_UNICODE
            }
        });
        inputs.Add(new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Ki = new NativeMethods.KeybdInput
            {
                WScan = c,
                DwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
            }
        });
    }
}
