namespace Autocorrect.App;

internal static class InputAnchorTracker
{
    private const int RecentClickMaxAgeMs = 120_000;
    private static readonly object Gate = new();
    private static int _x;
    private static int _y;
    private static nint _rootWindow;
    private static long _timestamp;
    private static long _clickVersion;
    private static int _pointerX;
    private static int _pointerY;
    private static nint _pointerRootWindow;
    private static long _pointerTimestamp;

    public static void RecordMouseClick(int x, int y, nint fallbackForegroundWindow)
    {
        var point = new NativeMethods.Point { X = x, Y = y };
        var clickedWindow = NativeMethods.WindowFromPoint(point);
        var rootWindow = clickedWindow != nint.Zero
            ? NativeMethods.GetAncestor(clickedWindow, NativeMethods.GA_ROOT)
            : NativeMethods.GetAncestor(fallbackForegroundWindow, NativeMethods.GA_ROOT);

        lock (Gate)
        {
            _x = x;
            _y = y;
            _rootWindow = rootWindow;
            _timestamp = Environment.TickCount64;
            _clickVersion++;
        }
    }

    public static long CurrentClickVersion
    {
        get
        {
            lock (Gate)
            {
                return _clickVersion;
            }
        }
    }

    public static bool TryGetRecentClick(out int x, out int y)
    {
        lock (Gate)
        {
            var age = Environment.TickCount64 - _timestamp;
            var foregroundRoot = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT);
            if (_timestamp == 0 ||
                age < 0 ||
                age > RecentClickMaxAgeMs ||
                (_rootWindow != nint.Zero && foregroundRoot != nint.Zero && _rootWindow != foregroundRoot))
            {
                x = 0;
                y = 0;
                return false;
            }

            x = _x;
            y = _y;
            return true;
        }
    }

    public static void RecordTypingPointerHint()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundRoot = NativeMethods.GetAncestor(foregroundWindow, NativeMethods.GA_ROOT);
        lock (Gate)
        {
            _pointerX = point.X;
            _pointerY = point.Y;
            _pointerRootWindow = foregroundRoot;
            _pointerTimestamp = Environment.TickCount64;
        }
    }

    public static bool TryGetRecentPointerHint(out int x, out int y)
    {
        lock (Gate)
        {
            var age = Environment.TickCount64 - _pointerTimestamp;
            var foregroundRoot = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT);
            if (_pointerTimestamp == 0 ||
                age < 0 ||
                age > 10_000 ||
                (_pointerRootWindow != nint.Zero && foregroundRoot != nint.Zero && _pointerRootWindow != foregroundRoot))
            {
                x = 0;
                y = 0;
                return false;
            }

            x = _pointerX;
            y = _pointerY;
            return true;
        }
    }
}
