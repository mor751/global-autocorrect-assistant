using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;

namespace Autocorrect.App;

// Bridges the foreground app and our UI: grabs the current selection and pastes a rewrite back into it.
public static class SelectionTextBridge
{
    // Grabs the text to rewrite: the current selection if any, otherwise the whole field (auto select-all).
    public static async Task<string> CaptureSelectionAsync()
    {
        var original = ReadClipboard();
        var captured = await CopyCurrentAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(captured))
        {
            SendCtrlChord(NativeMethods.VK_A);
            await Task.Delay(70).ConfigureAwait(true);
            captured = await CopyCurrentAsync().ConfigureAwait(true);
        }

        WriteClipboard(original);
        return captured;
    }

    // Clears the clipboard first so a failed/empty copy can never return stale text.
    private static async Task<string> CopyCurrentAsync()
    {
        WriteClipboard(string.Empty);
        await Task.Delay(30).ConfigureAwait(true);
        SendCtrlChord(NativeMethods.VK_C);
        await Task.Delay(130).ConfigureAwait(true);
        return ReadClipboard();
    }

    // Focuses the original window and pastes the given text, then restores the clipboard.
    public static async Task PasteIntoAsync(nint targetWindow, string text)
    {
        var original = ReadClipboard();
        if (targetWindow != nint.Zero)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(120).ConfigureAwait(true);
        }

        WriteClipboard(text);
        await Task.Delay(40).ConfigureAwait(true);
        SendCtrlChord(NativeMethods.VK_V);
        await Task.Delay(120).ConfigureAwait(true);
        WriteClipboard(original);
    }

    private static void SendCtrlChord(int virtualKey)
    {
        var inputs = new[]
        {
            KeyInput(NativeMethods.VK_CONTROL, false),
            KeyInput(virtualKey, false),
            KeyInput(virtualKey, true),
            KeyInput(NativeMethods.VK_CONTROL, true)
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static NativeMethods.Input KeyInput(int virtualKey, bool keyUp)
    {
        return new NativeMethods.Input
        {
            Type = NativeMethods.INPUT_KEYBOARD,
            Ki = new NativeMethods.KeybdInput
            {
                WVk = (ushort)virtualKey,
                DwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0
            }
        };
    }

    private static string ReadClipboard()
    {
        return OnUiThread(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
    }

    private static void WriteClipboard(string text)
    {
        OnUiThread(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    Clipboard.Clear();
                }
                else
                {
                    Clipboard.SetText(text);
                }
            }
            catch
            {
                // Clipboard can be briefly locked by another process; ignore and move on.
            }

            return true;
        });
    }

    private static T OnUiThread<T>(Func<T> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }
}
