using System.Windows.Interop;

namespace Autocorrect.App;

public sealed class HotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();

    public HotKeyManager()
    {
        var parameters = new HwndSourceParameters("GlobalAutocorrectHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0x800000
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Register(int id, uint virtualKey, Action action)
    {
        _actions[id] = action;
        NativeMethods.RegisterHotKey(_source.Handle, id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, virtualKey);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return nint.Zero;
    }
}
