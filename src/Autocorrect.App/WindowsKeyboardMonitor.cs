using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class WindowsKeyboardMonitor : IKeyboardMonitor
{
    private nint _hookHandle;
    private nint _mouseHookHandle;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;

    public event EventHandler<TypedKeyEventArgs>? KeyTyped;

    public void Start()
    {
        if (_hookHandle != nint.Zero)
        {
            return;
        }

        _hookProc = HookCallback;
        _mouseHookProc = MouseHookCallback;
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(currentModule?.ModuleName);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
        if (_hookHandle == nint.Zero)
        {
            throw new InvalidOperationException("Could not install the global keyboard hook.");
        }

        _mouseHookHandle = NativeMethods.SetWindowsHookExMouse(NativeMethods.WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);
    }

    public void Stop()
    {
        if (_hookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }

        if (_mouseHookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = nint.Zero;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN))
            {
                var data = Marshal.PtrToStructure<NativeMethods.Kbdllhookstruct>(lParam);
                if ((data.Flags & NativeMethods.LLKHF_INJECTED) == 0)
                {
                    if (PublishKey(data))
                    {
                        return 1;
                    }
                }
            }
        }
        catch
        {
            // Global hook callbacks must stay fast and must never throw into user32.
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == NativeMethods.WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MouseHookStruct>(lParam);
                InputAnchorTracker.RecordMouseClick(data.Pt.X, data.Pt.Y, NativeMethods.GetForegroundWindow());
            }
        }
        catch
        {
            // Mouse hooks share the same user32 callback safety rule as keyboard hooks.
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private bool PublishKey(NativeMethods.Kbdllhookstruct data)
    {
        if (IsResetModifier(data.VkCode) || IsSystemShortcutChord(data.VkCode))
        {
            return Publish(new TypedKeyEventArgs(TypedKeyKind.Reset));
        }

        if (IsForegroundBrowser() &&
            data.VkCode is NativeMethods.VK_RETURN or NativeMethods.VK_UP or NativeMethods.VK_DOWN)
        {
            Publish(new TypedKeyEventArgs(TypedKeyKind.Reset));
            return false;
        }

        switch (data.VkCode)
        {
            case NativeMethods.VK_BACK:
                return Publish(new TypedKeyEventArgs(TypedKeyKind.Backspace));
            case NativeMethods.VK_RETURN:
                return Publish(new TypedKeyEventArgs(TypedKeyKind.AcceptSuggestion));
            case NativeMethods.VK_UP:
                return Publish(new TypedKeyEventArgs(TypedKeyKind.NavigationUp));
            case NativeMethods.VK_DOWN:
                return Publish(new TypedKeyEventArgs(TypedKeyKind.NavigationDown));
            case NativeMethods.VK_TAB:
            case NativeMethods.VK_ESCAPE:
                return Publish(new TypedKeyEventArgs(TypedKeyKind.Reset));
            case NativeMethods.VK_SPACE:
                return Publish(new TypedKeyEventArgs(TypedKeyKind.Delimiter, ' '));
        }

        var translated = TranslateKey(data);
        if (translated is null)
        {
            return false;
        }

        var c = translated.Value;
        if (char.IsLetter(c) || c == '\'')
        {
            InputAnchorTracker.RecordTypingPointerHint();
            return Publish(new TypedKeyEventArgs(TypedKeyKind.Character, c));
        }
        else if (IsWordDelimiter(c))
        {
            return Publish(new TypedKeyEventArgs(TypedKeyKind.Delimiter, c));
        }
        else
        {
            return Publish(new TypedKeyEventArgs(TypedKeyKind.Reset));
        }
    }

    private bool Publish(TypedKeyEventArgs args)
    {
        KeyTyped?.Invoke(this, args);
        return args.Handled;
    }

    private static char? TranslateKey(NativeMethods.Kbdllhookstruct data)
    {
        var keyboardState = new byte[256];
        if (!NativeMethods.GetKeyboardState(keyboardState))
        {
            return null;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var layout = NativeMethods.GetKeyboardLayout(threadId);
        var buffer = new StringBuilder(8);
        var length = NativeMethods.ToUnicodeEx(
            data.VkCode,
            data.ScanCode,
            keyboardState,
            buffer,
            buffer.Capacity,
            0,
            layout);

        return length > 0 ? buffer[0] : null;
    }

    private static bool IsWordDelimiter(char c)
    {
        return c is '.' or ',' or '?' or '!' or ';' or ':' or ')' or ']' or '}' or '"' or '\u201D' or '\u2019'
            or '<' or '>' or '=' or '+' or '-' or '*' or '/' or '\\';
    }

    private static bool IsResetModifier(uint virtualKey)
    {
        return virtualKey is NativeMethods.VK_CONTROL
            or NativeMethods.VK_LCONTROL
            or NativeMethods.VK_RCONTROL
            or NativeMethods.VK_MENU
            or NativeMethods.VK_LMENU
            or NativeMethods.VK_RMENU
            or NativeMethods.VK_LWIN
            or NativeMethods.VK_RWIN;
    }

    private static bool IsSystemShortcutChord(uint virtualKey)
    {
        if (virtualKey == NativeMethods.VK_SHIFT)
        {
            return false;
        }

        return IsKeyDown(NativeMethods.VK_CONTROL) ||
               IsKeyDown(NativeMethods.VK_LCONTROL) ||
               IsKeyDown(NativeMethods.VK_RCONTROL) ||
               IsKeyDown(NativeMethods.VK_MENU) ||
               IsKeyDown(NativeMethods.VK_LMENU) ||
               IsKeyDown(NativeMethods.VK_RMENU) ||
               IsKeyDown(NativeMethods.VK_LWIN) ||
               IsKeyDown(NativeMethods.VK_RWIN);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
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
}
