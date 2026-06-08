using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autocorrect.Core;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace Autocorrect.App;

public partial class AiPetWindow : Window
{
    private readonly CorrectionSettings _settings;
    private readonly SettingsRepository _settingsRepository;
    private readonly Action<AiRewriteAction, nint> _runAction;
    private readonly Action _openDashboard;
    private readonly Action _setPetImage;
    private readonly DispatcherTimer _frameTimer = new();
    private BitmapImage[] _frames = Array.Empty<BitmapImage>();
    private int _frameIndex;
    private WpfPoint _dragStart;
    private bool _isDragging;
    private nint _targetWindow;

    public AiPetWindow(
        CorrectionSettings settings,
        SettingsRepository settingsRepository,
        Action<AiRewriteAction, nint> runAction,
        Action openDashboard,
        Action setPetImage)
    {
        InitializeComponent();
        _settings = settings;
        _settingsRepository = settingsRepository;
        _runAction = runAction;
        _openDashboard = openDashboard;
        _setPetImage = setPetImage;
        _frameTimer.Tick += OnFrameTick;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_NOACTIVATE);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyAppearance();
        Left = _settings.AiPetLeft ?? SystemParameters.WorkArea.Right - Width - 28;
        Top = _settings.AiPetTop ?? SystemParameters.WorkArea.Bottom - Height - 48;
    }

    // Chooses the pet visual: an animated frame sequence, a single image, or the built-in robot.
    private void ApplyAppearance()
    {
        _frameTimer.Stop();
        _frames = Array.Empty<BitmapImage>();
        PetRoot.ToolTip = string.IsNullOrWhiteSpace(_settings.AiPetName) ? "AI Pet" : _settings.AiPetName;

        var framePaths = _settings.AiPetFrames.Where(File.Exists).ToList();
        if (framePaths.Count > 1)
        {
            _frames = framePaths.Select(LoadBitmap).ToArray();
            _frameIndex = 0;
            PetImage.Source = _frames[0];
            ShowImage(_frames[0]);
            _frameTimer.Interval = TimeSpan.FromMilliseconds(_settings.AiPetFrameIntervalMs);
            _frameTimer.Start();
        }
        else if (!string.IsNullOrWhiteSpace(_settings.AiPetImagePath) && File.Exists(_settings.AiPetImagePath))
        {
            var bitmap = LoadBitmap(_settings.AiPetImagePath);
            PetImage.Source = bitmap;
            ShowImage(bitmap);
        }
        else
        {
            Width = 150;
            Height = 184;
            PetImage.Visibility = Visibility.Collapsed;
            RobotVisual.Visibility = Visibility.Visible;
        }
    }

    // Sizes the window to the artwork's aspect ratio so the pet fills the frame with no empty padding.
    private void ShowImage(BitmapImage bitmap)
    {
        const double targetHeight = 208;
        var ratio = bitmap.PixelWidth / (double)Math.Max(1, bitmap.PixelHeight);
        Height = targetHeight;
        Width = Math.Clamp(targetHeight * ratio, 90, 360);
        PetImage.Visibility = Visibility.Visible;
        RobotVisual.Visibility = Visibility.Collapsed;
    }

    // Switches the pet between idle and a "thinking/nibbling" state while a prompt is being analyzed.
    public void SetThinking(bool thinking)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetThinking(thinking));
            return;
        }

        BadgeText.Text = thinking ? "..." : "AI";
        PetContent.Opacity = thinking ? 0.78 : 1.0;
        PetRoot.ToolTip = thinking ? "Thinking…" : (string.IsNullOrWhiteSpace(_settings.AiPetName) ? "AI Pet" : _settings.AiPetName);
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_frames.Length == 0)
        {
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Length;
        PetImage.Source = _frames[_frameIndex];
    }

    // Decodes at the pet's display height to keep memory and scaling cost tiny.
    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelHeight = 320;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        base.OnClosed(e);
    }

    private void PetRoot_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _targetWindow = NativeMethods.GetForegroundWindow();
        _dragStart = e.GetPosition(this);
        _isDragging = false;
        PetRoot.CaptureMouse();
    }

    private void PetRoot_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!PetRoot.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = PointToScreen(e.GetPosition(this));
        var nextLeft = position.X - _dragStart.X;
        var nextTop = position.Y - _dragStart.Y;
        if (!_isDragging && Math.Abs(nextLeft - Left) + Math.Abs(nextTop - Top) < 4)
        {
            return;
        }

        _isDragging = true;
        Left = Math.Clamp(nextLeft, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(nextTop, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
    }

    private void PetRoot_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetRoot.ReleaseMouseCapture();
        if (_isDragging)
        {
            SavePosition();
            return;
        }

        PopupTitle.Text = string.IsNullOrWhiteSpace(_settings.AiPetName) ? "Beaver" : _settings.AiPetName;
        ActionsPopup.IsOpen = !ActionsPopup.IsOpen;
    }

    private void Action_OnClick(object sender, RoutedEventArgs e)
    {
        ActionsPopup.IsOpen = false;
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<AiRewriteAction>(tag, out var action))
        {
            _runAction(action, _targetWindow);
        }
    }

    private void Dashboard_OnClick(object sender, RoutedEventArgs e)
    {
        ActionsPopup.IsOpen = false;
        _openDashboard();
    }

    private void SetImage_OnClick(object sender, RoutedEventArgs e)
    {
        ActionsPopup.IsOpen = false;
        _setPetImage();
    }

    private void SavePosition()
    {
        _settings.AiPetLeft = Left;
        _settings.AiPetTop = Top;
        _settingsRepository.Save(_settings);
    }
}
