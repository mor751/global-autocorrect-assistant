using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using Autocorrect.Core;

namespace Autocorrect.App;

public interface ITextSuggestionPresenter
{
    event EventHandler<string>? SuggestionAccepted;

    void Show(IReadOnlyList<WordSuggestion> suggestions);

    void Hide();

    bool IsVisible { get; }

    void MoveSelection(int delta);

    bool AcceptSelected();
}

public sealed class FloatingSuggestionPresenter : ITextSuggestionPresenter
{
    private readonly Dispatcher _dispatcher;
    private SuggestionPopupWindow? _window;

    public event EventHandler<string>? SuggestionAccepted;

    public bool IsVisible => _window?.IsVisible == true;

    public FloatingSuggestionPresenter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Show(IReadOnlyList<WordSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            Hide();
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            _window ??= CreateWindow();
            var clickVersion = InputAnchorTracker.CurrentClickVersion;
            var keepManualPosition = _window.IsManuallyPositioned &&
                                     _window.ManualPlacementClickVersion == clickVersion;
            var anchor = TryGetInputAnchorRect();
            if (anchor is null)
            {
                if (keepManualPosition)
                {
                    _window.ShowSuggestions(suggestions, _window.Left, _window.Top, keepManualPosition: true);
                    return;
                }

                _window.HideSuggestions();
                return;
            }

            if (!keepManualPosition)
            {
                _window.ClearManualPlacement();
            }

            const double popupWidth = 280;
            var estimatedHeight = 36 + suggestions.Count * 38;
            var position = PlacePopupNearAnchor(anchor.Value, popupWidth, estimatedHeight);
            _window.ShowSuggestions(suggestions, position.Left, position.Top, keepManualPosition);
        });
    }

    public void Hide()
    {
        _dispatcher.BeginInvoke(() => _window?.HideSuggestions());
    }

    public void MoveSelection(int delta)
    {
        _dispatcher.Invoke(() => _window?.MoveSelection(delta));
    }

    public bool AcceptSelected()
    {
        return _dispatcher.Invoke(() => _window?.AcceptSelected() == true);
    }

    private SuggestionPopupWindow CreateWindow()
    {
        var window = new SuggestionPopupWindow();
        window.SuggestionAccepted += (_, suggestion) => SuggestionAccepted?.Invoke(this, suggestion);
        return window;
    }

    internal static ScreenAnchor? TryGetInputAnchorRect()
    {
        return TryFindEditableAnchorInForegroundWindow() ??
               TryGetRecentClickAnchor() ??
               TryGetUiAutomationAnchor() ??
               TryGetWin32CaretAnchor();
    }

    internal static PopupPosition PlacePopupNearAnchor(ScreenAnchor anchor, double popupWidth, double popupHeight)
    {
        var anchorPoint = new System.Drawing.Point((int)Math.Round(anchor.Left), (int)Math.Round(anchor.Top));
        var workArea = System.Windows.Forms.Screen.FromPoint(anchorPoint).WorkingArea;
        const double gap = 8;
        var workLeft = workArea.Left + gap;
        var workTop = workArea.Top + gap;
        var workRight = workArea.Right - gap;
        var workBottom = workArea.Bottom - gap;

        var anchorCenter = anchor.Left + (anchor.Width / 2);
        var left = Clamp(anchorCenter - (popupWidth / 2), workLeft, Math.Max(workLeft, workRight - popupWidth));

        var aboveTop = anchor.Top - popupHeight - gap;
        var belowTop = anchor.Bottom + gap;
        double top;

        if (aboveTop >= workTop)
        {
            top = aboveTop;
        }
        else if (belowTop + popupHeight <= workBottom)
        {
            top = belowTop;
        }
        else
        {
            var spaceAbove = Math.Max(0, anchor.Top - workTop);
            var spaceBelow = Math.Max(0, workBottom - anchor.Bottom);
            top = spaceAbove >= spaceBelow
                ? Clamp(aboveTop, workTop, Math.Max(workTop, workBottom - popupHeight))
                : Clamp(belowTop, workTop, Math.Max(workTop, workBottom - popupHeight));
        }

        return new PopupPosition(left, top);
    }

    private static ScreenAnchor? TryGetUiAutomationAnchor()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return null;
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
                textPatternObject is TextPattern textPattern)
            {
                foreach (var range in textPattern.GetSelection())
                {
                    foreach (var rect in range.GetBoundingRectangles())
                    {
                        if (TryCreateAnchor(rect, allowCaretSized: true) is { } anchor)
                        {
                            return anchor;
                        }
                    }
                }
            }

            var focusedRect = focused.Current.BoundingRectangle;
            return TryCreateAnchor(focusedRect, allowCaretSized: false);
        }
        catch
        {
            return null;
        }
    }

    private static ScreenAnchor? TryFindEditableAnchorInForegroundWindow()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == nint.Zero)
            {
                return null;
            }

            var root = AutomationElement.FromHandle(foreground);
            if (root is null)
            {
                return null;
            }

            var target = TryGetAnchorTargetPoint();
            var best = default(EditableAnchorCandidate?);
            AddElementAndParents(AutomationElement.FocusedElement, target, ref best);

            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true),
                new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true));

            var elements = root.FindAll(TreeScope.Descendants, condition);
            var max = Math.Min(elements.Count, 500);
            for (var i = 0; i < max; i++)
            {
                AddCandidate(elements[i], target, ref best);
            }

            return best?.Anchor;
        }
        catch
        {
            return null;
        }
    }

    private static void AddElementAndParents(AutomationElement? element, AnchorTarget? target, ref EditableAnchorCandidate? best)
    {
        for (var depth = 0; element is not null && depth < 7; depth++)
        {
            AddCandidate(element, target, ref best);
            try
            {
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            catch
            {
                return;
            }
        }
    }

    private static void AddCandidate(AutomationElement element, AnchorTarget? target, ref EditableAnchorCandidate? best)
    {
        try
        {
            if (!LooksEditable(element))
            {
                return;
            }

            var rect = element.Current.BoundingRectangle;
            if (TryCreateAnchor(rect, allowCaretSized: false) is not { } anchor)
            {
                return;
            }

            var score = ScoreAnchor(anchor, target);
            if (best is null || score < best.Value.Score)
            {
                best = new EditableAnchorCandidate(anchor, score);
            }
        }
        catch
        {
            // Individual UIA elements can become stale while the user types.
        }
    }

    private static bool LooksEditable(AutomationElement element)
    {
        var controlType = element.Current.ControlType;
        if (controlType == ControlType.Edit)
        {
            return true;
        }

        var hasTextPattern = (bool)element.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty, true);
        var hasValuePattern = (bool)element.GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty, true);
        return hasTextPattern || hasValuePattern;
    }

    private static AnchorTarget? TryGetAnchorTargetPoint()
    {
        if (InputAnchorTracker.TryGetRecentClick(out var clickX, out var clickY))
        {
            return new AnchorTarget(clickX, clickY, 0);
        }

        if (InputAnchorTracker.TryGetRecentPointerHint(out var pointerX, out var pointerY))
        {
            return new AnchorTarget(pointerX, pointerY, 12_000);
        }

        return null;
    }

    private static double ScoreAnchor(ScreenAnchor anchor, AnchorTarget? target)
    {
        var areaPenalty = Math.Min(50_000, anchor.Width * anchor.Height) / 5_000;
        if (target is null)
        {
            return areaPenalty;
        }

        if (target.Value.X >= anchor.Left &&
            target.Value.X <= anchor.Right &&
            target.Value.Y >= anchor.Top &&
            target.Value.Y <= anchor.Bottom)
        {
            return target.Value.BasePenalty + areaPenalty;
        }

        var dx = target.Value.X < anchor.Left
            ? anchor.Left - target.Value.X
            : target.Value.X > anchor.Right
                ? target.Value.X - anchor.Right
                : 0;
        var dy = target.Value.Y < anchor.Top
            ? anchor.Top - target.Value.Y
            : target.Value.Y > anchor.Bottom
                ? target.Value.Y - anchor.Bottom
                : 0;

        return target.Value.BasePenalty + (dx * dx) + (dy * dy) + areaPenalty;
    }

    private static ScreenAnchor? TryGetRecentClickAnchor()
    {
        if (!InputAnchorTracker.TryGetRecentClick(out var x, out var y))
        {
            return null;
        }

        try
        {
            var clickPoint = new System.Windows.Point(x, y);
            var element = AutomationElement.FromPoint(clickPoint);
            for (var depth = 0; element is not null && depth < 5; depth++)
            {
                var rect = element.Current.BoundingRectangle;
                if (Contains(rect, x, y) &&
                    TryCreateAnchor(rect, allowCaretSized: false) is { } anchor)
                {
                    return anchor;
                }

                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
        }
        catch
        {
            // Some browser-hosted controls reject UIA inspection; a point anchor is still better than a wrong caret.
        }

        return TryCreatePointAnchor(x, y);
    }

    private static ScreenAnchor? TryGetWin32CaretAnchor()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var thread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new NativeMethods.GuiThreadInfo
        {
            CbSize = Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };

        if (NativeMethods.GetGUIThreadInfo(thread, ref info) && info.HwndCaret != nint.Zero)
        {
            var point = new NativeMethods.Point
            {
                X = info.RcCaret.Left,
                Y = info.RcCaret.Top
            };

            if (NativeMethods.ClientToScreen(info.HwndCaret, ref point))
            {
                var width = Math.Max(2, info.RcCaret.Right - info.RcCaret.Left);
                var height = Math.Max(18, info.RcCaret.Bottom - info.RcCaret.Top);
                return new ScreenAnchor(point.X, point.Y, point.X + width, point.Y + height);
            }
        }

        return null;
    }

    private static ScreenAnchor? TryCreateAnchor(Rect rect, bool allowCaretSized)
    {
        if (rect.IsEmpty ||
            double.IsNaN(rect.Left) ||
            double.IsNaN(rect.Top) ||
            double.IsInfinity(rect.Left) ||
            double.IsInfinity(rect.Top) ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            return null;
        }

        if (!allowCaretSized && (rect.Width < 24 || rect.Height < 18))
        {
            return null;
        }

        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)Math.Round(rect.Left), (int)Math.Round(rect.Top)));
        var workArea = screen.WorkingArea;

        if (rect.Right < workArea.Left ||
            rect.Left > workArea.Right ||
            rect.Bottom < workArea.Top ||
            rect.Top > workArea.Bottom)
        {
            return null;
        }

        var looksLikeWholeWindow = rect.Width > Math.Min(1200, workArea.Width * 0.92) &&
                                   rect.Height > Math.Min(650, workArea.Height * 0.7);
        if (looksLikeWholeWindow)
        {
            return null;
        }

        return new ScreenAnchor(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static ScreenAnchor? TryCreatePointAnchor(int x, int y)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
        var workArea = screen.WorkingArea;
        if (x < workArea.Left || x > workArea.Right || y < workArea.Top || y > workArea.Bottom)
        {
            return null;
        }

        return new ScreenAnchor(x - 10, y - 12, x + 10, y + 18);
    }

    private static bool Contains(Rect rect, int x, int y)
    {
        return !rect.IsEmpty &&
               x >= rect.Left &&
               x <= rect.Right &&
               y >= rect.Top &&
               y <= rect.Bottom;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}

public readonly record struct ScreenAnchor(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);

    public double Height => Math.Max(0, Bottom - Top);
}

public readonly record struct PopupPosition(double Left, double Top);

internal readonly record struct AnchorTarget(double X, double Y, double BasePenalty);

internal readonly record struct EditableAnchorCandidate(ScreenAnchor Anchor, double Score);

public sealed class NullSuggestionPresenter : ITextSuggestionPresenter
{
    public static NullSuggestionPresenter Instance { get; } = new();

    public event EventHandler<string>? SuggestionAccepted
    {
        add { }
        remove { }
    }

    private NullSuggestionPresenter()
    {
    }

    public void Show(IReadOnlyList<WordSuggestion> suggestions)
    {
    }

    public void Hide()
    {
    }

    public bool IsVisible => false;

    public void MoveSelection(int delta)
    {
    }

    public bool AcceptSelected()
    {
        return false;
    }
}

public interface IFloatingOverlayService
{
    void Schedule(string text, AppContextSnapshot context);

    void Hide();
}

public sealed class FloatingImproveOverlayService : IFloatingOverlayService
{
    private readonly Dispatcher _dispatcher;
    private readonly CorrectionSettings _settings;
    private readonly IAiRewriteService _rewriteService;
    private readonly ITextReplacer _textReplacer;
    private readonly DispatcherTimer _timer;
    private ImproveOverlayWindow? _pill;
    private string _latestText = string.Empty;
    private AppContextSnapshot _latestContext = AppContextSnapshot.Unknown;

    public FloatingImproveOverlayService(
        Dispatcher dispatcher,
        CorrectionSettings settings,
        IAiRewriteService rewriteService,
        ITextReplacer textReplacer)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _rewriteService = rewriteService;
        _textReplacer = textReplacer;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += (_, _) => ShowPill();
    }

    public void Schedule(string text, AppContextSnapshot context)
    {
        if (!_settings.Enabled ||
            !_settings.AiOverlayEnabled ||
            !_settings.ShowFloatingPill ||
            !_rewriteService.IsEnabled(_settings) ||
            context.IsSensitive ||
            !context.IsAllowedForAiOverlay ||
            text.Length < 40 ||
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < _settings.MinWordsForOverlay)
        {
            Hide();
            return;
        }

        _latestText = text;
        _latestContext = context;
        _dispatcher.BeginInvoke(() =>
        {
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(_settings.FloatingPillDelayMs);
            _timer.Start();
        });
    }

    public void Hide()
    {
        _dispatcher.BeginInvoke(() =>
        {
            _timer.Stop();
            _pill?.Hide();
        });
    }

    private void ShowPill()
    {
        _timer.Stop();
        _pill ??= CreatePill();
        var anchor = FloatingSuggestionPresenter.TryGetInputAnchorRect();
        if (anchor is null)
        {
            return;
        }

        var position = FloatingSuggestionPresenter.PlacePopupNearAnchor(anchor.Value, 118, 38);
        _pill.Left = position.Left;
        _pill.Top = position.Top;
        _pill.Show();
    }

    private ImproveOverlayWindow CreatePill()
    {
        var pill = new ImproveOverlayWindow();
        pill.ImproveClicked += (_, _) =>
        {
            pill.Hide();
            var modal = new ImproveTextWindow(_latestText, _rewriteService, _settings, _latestContext);
            if (modal.ShowDialog() == true && !string.IsNullOrWhiteSpace(modal.ReplacementText))
            {
                _textReplacer.ReplaceCurrentWord(_latestText, modal.ReplacementText);
            }
        };
        return pill;
    }
}

public sealed class NullFloatingOverlayService : IFloatingOverlayService
{
    public static NullFloatingOverlayService Instance { get; } = new();

    private NullFloatingOverlayService()
    {
    }

    public void Schedule(string text, AppContextSnapshot context)
    {
    }

    public void Hide()
    {
    }
}
