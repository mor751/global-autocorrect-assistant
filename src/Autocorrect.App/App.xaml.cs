using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Autocorrect.Core;
using Application = System.Windows.Application;

namespace Autocorrect.App;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private AutocorrectController? _controller;
    private SettingsRepository? _settingsRepository;
    private CorrectionSettings? _settings;
    private WindowsKeyboardMonitor? _keyboardMonitor;
    private SymSpellCorrectionEngine? _correctionEngine;
    private RuntimeStatusStore? _runtimeStatus;
    private CorrectionHistoryStore? _correctionHistory;
    private CompositeAiRewriteService? _aiRewriteService;
    private HotKeyManager? _hotKeyManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _settingsRepository = new SettingsRepository();
        _settings = _settingsRepository.Load();

        var recentCorrections = new RecentCorrectionStore();
        _correctionHistory = new CorrectionHistoryStore(_settings.CorrectionHistoryLimit);
        _runtimeStatus = new RuntimeStatusStore();
        _keyboardMonitor = new WindowsKeyboardMonitor();
        _correctionEngine = new SymSpellCorrectionEngine();
        _aiRewriteService = new CompositeAiRewriteService();
        var textReplacer = new SendInputTextReplacer(_settings);
        SymSpellCorrectionEngine.WarmUp();
        _controller = new AutocorrectController(
            _keyboardMonitor,
            new WindowsTextContextDetector(),
            textReplacer,
            _correctionEngine,
            _settings,
            recentCorrections,
            _runtimeStatus,
            new PersistentWordLearningStore(_settings, _settingsRepository),
            _correctionHistory,
            new FloatingSuggestionPresenter(Dispatcher),
            new FloatingImproveOverlayService(Dispatcher, _settings, _aiRewriteService, textReplacer));

        _notifyIcon = BuildTrayIcon(recentCorrections, _runtimeStatus);
        _notifyIcon.Visible = true;

        RegisterHotKeys();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _hotKeyManager?.Dispose();
        _aiRewriteService?.Dispose();
        _keyboardMonitor?.Dispose();

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.OnExit(e);
    }

    private NotifyIcon BuildTrayIcon(RecentCorrectionStore recentCorrections, RuntimeStatusStore runtimeStatus)
    {
        var menu = new ContextMenuStrip();
        var status = new ToolStripMenuItem("Global Autocorrect: running") { Enabled = false };
        var toggle = new ToolStripMenuItem();
        var toggleOverlay = new ToolStripMenuItem();
        var pause15 = new ToolStripMenuItem("Pause for 15 minutes");
        var undo = new ToolStripMenuItem("Undo last correction");
        var settings = new ToolStripMenuItem("Settings...");
        var recent = new ToolStripMenuItem("Recent corrections...");
        var quit = new ToolStripMenuItem("Quit");

        void RefreshToggle()
        {
            toggle.Text = _settings?.Enabled == true ? "Pause corrections" : "Resume corrections";
            toggleOverlay.Text = _settings?.AiOverlayEnabled == true ? "Disable floating AI overlay" : "Enable floating AI overlay";
            status.Text = _settings?.Enabled == true ? "Global Autocorrect: running" : "Global Autocorrect: paused";
        }

        toggle.Click += (_, _) =>
        {
            if (_settings is null || _settingsRepository is null)
            {
                return;
            }

            _settings.Enabled = !_settings.Enabled;
            _settingsRepository.Save(_settings);
            RefreshToggle();
        };

        toggleOverlay.Click += (_, _) =>
        {
            if (_settings is null || _settingsRepository is null)
            {
                return;
            }

            _settings.AiOverlayEnabled = !_settings.AiOverlayEnabled;
            _settingsRepository.Save(_settings);
            RefreshToggle();
        };

        pause15.Click += async (_, _) =>
        {
            if (_settings is null || _settingsRepository is null)
            {
                return;
            }

            _settings.Enabled = false;
            _settingsRepository.Save(_settings);
            RefreshToggle();
            await Task.Delay(TimeSpan.FromMinutes(15));
            _settings.Enabled = true;
            _settingsRepository.Save(_settings);
            RefreshToggle();
        };

        undo.Click += (_, _) => _controller?.UndoLastCorrection();

        settings.Click += (_, _) =>
        {
            if (_settings is null || _settingsRepository is null)
            {
                return;
            }

            new SettingsWindow(_settings, _settingsRepository, runtimeStatus).Show();
        };

        recent.Click += (_, _) => new RecentCorrectionsWindow(recentCorrections).Show();
        quit.Click += (_, _) => Shutdown();

        RefreshToggle();
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(toggle);
        menu.Items.Add(toggleOverlay);
        menu.Items.Add(pause15);
        menu.Items.Add(undo);
        menu.Items.Add(settings);
        menu.Items.Add(recent);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quit);

        var notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Global Autocorrect",
            ContextMenuStrip = menu
        };

        notifyIcon.DoubleClick += (_, _) =>
        {
            if (_settings is not null && _settingsRepository is not null)
            {
                new SettingsWindow(_settings, _settingsRepository, runtimeStatus).Show();
            }
        };

        return notifyIcon;
    }

    private void RegisterHotKeys()
    {
        if (_settings is null || _aiRewriteService is null)
        {
            return;
        }

        _hotKeyManager = new HotKeyManager();
        _hotKeyManager.Register(1, NativeMethods.VK_BACK, () => _controller?.UndoLastCorrection());
        _hotKeyManager.Register(2, NativeMethods.VK_PAUSE, TogglePaused);
        _hotKeyManager.Register(3, NativeMethods.VK_F, () => OpenImproveModal(AiRewriteAction.FixTyposOnly));
        _hotKeyManager.Register(4, NativeMethods.VK_O, () => OpenImproveModal(AiRewriteAction.OptimizePrompt));
        _hotKeyManager.Register(5, NativeMethods.VK_S, () => OpenImproveModal(AiRewriteAction.CompressTokens));
    }

    private void TogglePaused()
    {
        if (_settings is null || _settingsRepository is null)
        {
            return;
        }

        _settings.Enabled = !_settings.Enabled;
        _settingsRepository.Save(_settings);
    }

    private void OpenImproveModal(AiRewriteAction action)
    {
        if (_settings is null || _aiRewriteService is null)
        {
            return;
        }

        var context = new WindowsTextContextDetector().GetActiveContext(_settings);
        if (!context.IsAllowedForAiOverlay || context.IsSensitive)
        {
            return;
        }

        var window = new ImproveTextWindow(string.Empty, _aiRewriteService, _settings, context);
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _runtimeStatus?.RecordError(e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _runtimeStatus?.RecordError(exception);
        }
    }
}
