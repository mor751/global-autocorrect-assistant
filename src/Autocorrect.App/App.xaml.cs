using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Autocorrect.Core;
using Autocorrect.Core.Brain;
using Application = System.Windows.Application;

namespace Autocorrect.App;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private SettingsRepository? _settingsRepository;
    private CorrectionSettings? _settings;
    private RuntimeStatusStore? _runtimeStatus;
    private CompositeAiRewriteService? _aiRewriteService;
    private TokenUsageStore? _tokenUsage;
    private HotKeyManager? _hotKeyManager;
    private AiPetWindow? _aiPetWindow;
    private DashboardWindow? _dashboardWindow;
    private ProjectBrainService? _projectBrain;
    private ProjectBrainWindow? _projectBrainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0].Equals("--process-pet", StringComparison.OrdinalIgnoreCase))
        {
            ProcessPetFromCli(e.Args[1]);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _settingsRepository = new SettingsRepository();
        _settings = _settingsRepository.Load();
        _runtimeStatus = new RuntimeStatusStore();
        _aiRewriteService = new CompositeAiRewriteService();
        _tokenUsage = new TokenUsageStore();
        _projectBrain = new ProjectBrainService(SettingsRepository.DataDirectory, BuildBrainOptions(_settings));

        _notifyIcon = BuildTrayIcon();
        _notifyIcon.Visible = true;

        RegisterHotKeys();
        ShowAiPetIfEnabled();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKeyManager?.Dispose();
        _aiRewriteService?.Dispose();
        _projectBrain?.Dispose();
        _aiPetWindow?.Close();
        _dashboardWindow?.Close();
        _projectBrainWindow?.Close();

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
        }

        base.OnExit(e);
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        var status = new ToolStripMenuItem("AI Prompt Pet: running") { Enabled = false };
        var toggleAiPet = new ToolStripMenuItem();
        var setPetImage = new ToolStripMenuItem("Set pet image...");
        var dashboard = new ToolStripMenuItem("Token dashboard...");
        var selectProject = new ToolStripMenuItem("Select project folder...");
        var reindexProject = new ToolStripMenuItem("Re-index project");
        var openBrain = new ToolStripMenuItem("Open Project Brain...");
        var settings = new ToolStripMenuItem("Settings...");
        var quit = new ToolStripMenuItem("Quit");

        void RefreshToggle()
        {
            toggleAiPet.Text = _settings?.AiPetEnabled == true ? "Hide AI pet" : "Show AI pet";
        }

        toggleAiPet.Click += (_, _) =>
        {
            if (_settings is null || _settingsRepository is null)
            {
                return;
            }

            _settings.AiPetEnabled = !_settings.AiPetEnabled;
            _settingsRepository.Save(_settings);
            if (_settings.AiPetEnabled)
            {
                ShowAiPetIfEnabled();
            }
            else
            {
                _aiPetWindow?.Hide();
            }

            RefreshToggle();
        };

        setPetImage.Click += (_, _) => SetPetImage();
        dashboard.Click += (_, _) => OpenDashboard();
        selectProject.Click += (_, _) => SelectProjectFolder();
        reindexProject.Click += async (_, _) => await ReindexCurrentProjectAsync();
        openBrain.Click += (_, _) => OpenProjectBrain();

        settings.Click += (_, _) =>
        {
            if (_settings is not null && _settingsRepository is not null && _runtimeStatus is not null)
            {
                new SettingsWindow(_settings, _settingsRepository, _runtimeStatus).Show();
            }
        };

        quit.Click += (_, _) => Shutdown();

        RefreshToggle();
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(toggleAiPet);
        menu.Items.Add(setPetImage);
        menu.Items.Add(dashboard);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(selectProject);
        menu.Items.Add(reindexProject);
        menu.Items.Add(openBrain);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quit);

        var notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "AI Prompt Pet",
            ContextMenuStrip = menu
        };

        notifyIcon.DoubleClick += (_, _) => OpenDashboard();
        return notifyIcon;
    }

    private void RegisterHotKeys()
    {
        if (_settings is null || _aiRewriteService is null)
        {
            return;
        }

        _hotKeyManager = new HotKeyManager();
        _hotKeyManager.Register(1, NativeMethods.VK_O, () => RunInlineRewrite(AiRewriteAction.SmartOptimize, NativeMethods.GetForegroundWindow()));
        _hotKeyManager.Register(2, NativeMethods.VK_D, OpenDashboard);
    }

    private void ShowAiPetIfEnabled()
    {
        if (_settings is null || _settingsRepository is null || !_settings.AiPetEnabled)
        {
            return;
        }

        if (_aiPetWindow is null)
        {
            _aiPetWindow = new AiPetWindow(_settings, _settingsRepository, RunInlineRewrite, OpenDashboard, SetPetImage);
            _aiPetWindow.Closed += (_, _) => _aiPetWindow = null;
        }

        _aiPetWindow.Show();
    }

    // Headless mode: strips white backgrounds from a single image or a folder of frames, then saves to settings.
    private static void ProcessPetFromCli(string path)
    {
        var repository = new SettingsRepository();
        var settings = repository.Load();
        try
        {
            if (Directory.Exists(path))
            {
                settings.AiPetFrames = PetImageProcessor.ProcessFolderToFrames(path);
            }
            else if (File.Exists(path) && Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                settings.AiPetFrames = PetImageProcessor.ProcessGifToFrames(path, out var interval);
                settings.AiPetFrameIntervalMs = interval;
            }
            else if (File.Exists(path))
            {
                settings.AiPetFrames = new List<string>();
                settings.AiPetImagePath = PetImageProcessor.ProcessToTransparentPng(path);
            }

            settings.AiPetEnabled = true;
            repository.Save(settings);
            File.WriteAllText(
                Path.Combine(SettingsRepository.DataDirectory, "pet-cli-result.txt"),
                $"frames={settings.AiPetFrames.Count} image={settings.AiPetImagePath}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(SettingsRepository.DataDirectory, "pet-cli-error.txt"), ex.ToString());
        }
    }

    private void SetPetImage()
    {
        if (_settings is null || _settingsRepository is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a pet image (white background is fine)",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (Path.GetExtension(dialog.FileName).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                _settings.AiPetFrames = PetImageProcessor.ProcessGifToFrames(dialog.FileName, out var interval);
                _settings.AiPetFrameIntervalMs = interval;
            }
            else
            {
                _settings.AiPetFrames = new List<string>();
                _settings.AiPetImagePath = PetImageProcessor.ProcessToTransparentPng(dialog.FileName);
            }

            _settings.AiPetEnabled = true;
            _settingsRepository.Save(_settings);
            ReloadPet();
            Notify("Pet image updated.");
        }
        catch (Exception ex)
        {
            _runtimeStatus?.RecordError(ex);
            Notify("Couldn't process that image.");
        }
    }

    private void ReloadPet()
    {
        _aiPetWindow?.Close();
        _aiPetWindow = null;
        ShowAiPetIfEnabled();
    }

    private void OpenDashboard()
    {
        if (_tokenUsage is null)
        {
            return;
        }

        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow(_tokenUsage);
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
        }

        _dashboardWindow.Show();
        _dashboardWindow.Activate();
        _dashboardWindow.Refresh();
    }

    private async void RunInlineRewrite(AiRewriteAction action, nint targetWindow)
    {
        if (_settings is null || _aiRewriteService is null)
        {
            return;
        }

        if (action == AiRewriteAction.SmartOptimize && _settings.ProjectBrainEnabled && _projectBrain is not null)
        {
            RunProjectAwareEnhance(targetWindow);
            return;
        }

        if (targetWindow != nint.Zero)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(90);
        }

        var context = new WindowsTextContextDetector().GetActiveContext(_settings);
        if (context.IsSensitive)
        {
            return;
        }

        var selected = await SelectionTextBridge.CaptureSelectionAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            Notify("Put your cursor in the prompt box, then press the shortcut.");
            return;
        }

        Notify("Optimizing prompt...");
        AiRewriteResult? result;
        try
        {
            var request = new AiRewriteRequest(selected, action, context, string.Empty, string.Empty);
            result = await _aiRewriteService.RewriteAsync(request, _settings, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _runtimeStatus?.RecordError(ex);
            Notify("Rewrite failed.");
            return;
        }

        if (result is null || string.IsNullOrWhiteSpace(result.RewrittenText))
        {
            Notify(RewriteUnavailableMessage());
            return;
        }

        await SelectionTextBridge.PasteIntoAsync(targetWindow, result.RewrittenText);
        _tokenUsage?.Record(action.ToString(), selected, result.RewrittenText);
        _dashboardWindow?.Refresh();
        Notify($"Done. ~{result.EstimatedTokenReductionPercent}% fewer tokens.");
    }

    // Project-aware flow: capture prompt, retrieve project context, analyze, rewrite, and show the overlay.
    private async void RunProjectAwareEnhance(nint targetWindow)
    {
        if (_settings is null || _projectBrain is null)
        {
            return;
        }

        if (targetWindow != nint.Zero)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(90);
        }

        var context = new WindowsTextContextDetector().GetActiveContext(_settings);
        if (context.IsSensitive)
        {
            Notify("Skipped: this looks like a sensitive field.");
            return;
        }

        var selected = await SelectionTextBridge.CaptureSelectionAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            Notify("Put your cursor in the prompt box, then press Ctrl+Alt+O.");
            return;
        }

        _aiPetWindow?.SetThinking(true);
        EnhancementOutcome outcome;
        try
        {
            outcome = await _projectBrain.EnhanceAsync(selected, _settings.ProjectRoot, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _runtimeStatus?.RecordError(ex);
            Notify("Prompt enhancement failed.");
            return;
        }
        finally
        {
            _aiPetWindow?.SetThinking(false);
        }

        ShowPromptResult(outcome, targetWindow, selected);
    }

    private void ShowPromptResult(EnhancementOutcome outcome, nint targetWindow, string original)
    {
        var window = new PromptResultWindow(
            outcome,
            async text =>
            {
                await SelectionTextBridge.PasteIntoAsync(targetWindow, text);
                _tokenUsage?.Record(AiRewriteAction.SmartOptimize.ToString(), original, text);
                _dashboardWindow?.Refresh();
            },
            ReindexCurrentProjectAsync,
            () => OpenProjectBrain(outcome.UsedFiles));

        window.Show();
        window.Activate();
    }

    private async void SelectProjectFolder()
    {
        if (_settings is null || _settingsRepository is null)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog { Description = "Select your project root folder" };
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        _settings.ProjectRoot = dialog.SelectedPath;
        _settingsRepository.Save(_settings);
        await ReindexCurrentProjectAsync();
    }

    // Rebuilds the Project Brain index and vector store for the configured project root.
    private async Task ReindexCurrentProjectAsync()
    {
        if (_projectBrain is null || _settings is null || string.IsNullOrWhiteSpace(_settings.ProjectRoot))
        {
            Notify("Select a project folder first.");
            return;
        }

        _aiPetWindow?.SetThinking(true);
        try
        {
            var brain = await _projectBrain.IndexAsync(_settings.ProjectRoot, CancellationToken.None);
            Notify($"Indexed {brain.Files.Count} files in {brain.ProjectName}.");
        }
        catch (Exception ex)
        {
            _runtimeStatus?.RecordError(ex);
            Notify("Indexing failed.");
        }
        finally
        {
            _aiPetWindow?.SetThinking(false);
        }
    }

    private void OpenProjectBrain(IEnumerable<string>? highlight = null)
    {
        if (_projectBrain is null || _settings is null)
        {
            return;
        }

        var brain = _projectBrain.LoadBrain(_settings.ProjectRoot);
        if (brain is null)
        {
            Notify("Select and index a project folder first.");
            return;
        }

        _projectBrainWindow?.Close();
        _projectBrainWindow = new ProjectBrainWindow(brain, highlight ?? Array.Empty<string>(), ReindexCurrentProjectAsync);
        _projectBrainWindow.Closed += (_, _) => _projectBrainWindow = null;
        _projectBrainWindow.Show();
        _projectBrainWindow.Activate();
    }

    private static ProjectBrainOptions BuildBrainOptions(CorrectionSettings settings)
    {
        return new ProjectBrainOptions
        {
            Ollama = new OllamaSettings(settings.AiEndpoint, settings.AiModel, settings.EmbeddingModel),
            Index = new IndexOptions
            {
                MaxFileSizeBytes = settings.MaxIndexedFileSizeKb * 1024,
                MaxFiles = settings.MaxIndexedFiles,
                IgnoredFolders = settings.IgnoredProjectFolders.ToHashSet(StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private string RewriteUnavailableMessage()
    {
        if (_settings is not null && _settings.AiProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            var model = string.IsNullOrWhiteSpace(_settings.AiModel) ? "qwen2.5:3b" : _settings.AiModel;
            return $"Couldn't reach Ollama. Run 'ollama serve' and 'ollama pull {model}'.";
        }

        return "AI rewrite is disabled for this context.";
    }

    private void Notify(string text)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "AI Prompt Pet";
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(2500);
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
