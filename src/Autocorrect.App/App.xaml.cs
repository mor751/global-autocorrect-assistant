using System.Diagnostics;
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
    private RagWindow? _ragWindow;
    private string _projectBrainStatus = "no_folder";
    private string? _projectBrainLastError;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
        EnsureDefaultPetAppearance(_settings, _settingsRepository);
        var activeRoot = ActiveProjectStore.Get(_settings);
        if (!string.IsNullOrWhiteSpace(activeRoot))
        {
            _settings.ProjectRoot = activeRoot;
        }
        _projectBrainStatus = string.IsNullOrWhiteSpace(_settings.ProjectRoot) ? "no_folder" : "folder_selected";
        _runtimeStatus = new RuntimeStatusStore();
        _aiRewriteService = new CompositeAiRewriteService();
        _tokenUsage = new TokenUsageStore();
        _projectBrain = new ProjectBrainService(SettingsRepository.DataDirectory, BuildBrainOptions(_settings));
        if (!string.IsNullOrWhiteSpace(_settings.ProjectRoot) && _projectBrain.IsIndexed(_settings.ProjectRoot))
        {
            var metadata = _projectBrain.LoadIndexMetadata(_settings.ProjectRoot);
            _projectBrainStatus = StatusText(metadata?.Status ?? ProjectBrainStatus.Ready);
            _projectBrainLastError = metadata?.LastError;
        }

        _notifyIcon = BuildTrayIcon();
        _notifyIcon.Visible = true;

        RegisterHotKeys();
        ShowAiPetIfEnabled();
        OpenDashboard();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;
        _hotKeyManager?.Dispose();
        _aiRewriteService?.Dispose();
        _projectBrain?.Dispose();
        _aiPetWindow?.Close();
        _dashboardWindow?.Close();
        _projectBrainWindow?.Close();
        _ragWindow?.Close();

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
        var status = new ToolStripMenuItem("Woody: running") { Enabled = false };
        var openStudio = new ToolStripMenuItem("Open Woody Studio");
        var toggleAiPet = new ToolStripMenuItem();
        var setPetImage = new ToolStripMenuItem("Set pet image...");
        var dashboard = new ToolStripMenuItem("Dashboard...");
        var selectProject = new ToolStripMenuItem("Select project folder...");
        var reindexProject = new ToolStripMenuItem("Re-index project (full)");
        var reloadProject = new ToolStripMenuItem("Reload project files...");
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

        openStudio.Click += (_, _) => OpenDashboard();
        setPetImage.Click += (_, _) => SetPetImage();
        dashboard.Click += (_, _) => OpenDashboard();
        selectProject.Click += (_, _) => SelectProjectFolder();
        reindexProject.Click += async (_, _) => await ReindexCurrentProjectAsync();
        reloadProject.Click += async (_, _) => await ReloadCurrentProjectAsync();
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
        menu.Items.Add(openStudio);
        menu.Items.Add(dashboard);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(toggleAiPet);
        menu.Items.Add(setPetImage);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(selectProject);
        menu.Items.Add(reloadProject);
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

        notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                OpenDashboard();
            }
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
            _aiPetWindow = new AiPetWindow(
                _settings,
                _settingsRepository,
                RunInlineRewrite,
                OpenDashboard,
                SelectProjectFolder,
                ReindexCurrentProjectAsync,
                ShowVectorStoreStatus,
                OpenRag,
                () => _settings?.ProjectRoot,
                ProjectStatusText,
                SetPetImage);
            _aiPetWindow.Closed += (_, _) => _aiPetWindow = null;
        }

        _aiPetWindow.Show();
    }

    private static void EnsureDefaultPetAppearance(CorrectionSettings settings, SettingsRepository repository)
    {
        if (!string.IsNullOrWhiteSpace(settings.AiPetImagePath) ||
            settings.AiPetFrames.Any(File.Exists))
        {
            return;
        }

        var defaultGif = FindBundledDefaultPetGif();
        if (defaultGif is null)
        {
            return;
        }

        try
        {
            settings.AiPetFrames = PetImageProcessor.ProcessGifToFrames(defaultGif, out var interval);
            settings.AiPetFrameIntervalMs = interval;
            settings.AiPetImagePath = null;
            settings.AiPetEnabled = true;
            if (string.IsNullOrWhiteSpace(settings.AiPetName) ||
                settings.AiPetName.Equals("Pet", StringComparison.OrdinalIgnoreCase) ||
                settings.AiPetName.Equals("Beaver", StringComparison.OrdinalIgnoreCase))
            {
                settings.AiPetName = "Woody";
            }

            repository.Save(settings);
        }
        catch
        {
            // If the bundled GIF cannot be processed, keep the built-in robot as a last-resort fallback.
        }
    }

    private static string? FindBundledDefaultPetGif()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "1780836848157.gif"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "1780836848157.gif"),
            Path.Combine(Environment.CurrentDirectory, "1780836848157.gif"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "1780836848157.gif")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "1780836848157.gif"))
        };

        return candidates.FirstOrDefault(File.Exists);
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
        if (_tokenUsage is null || _settings is null || _projectBrain is null)
        {
            return;
        }

            if (_dashboardWindow is null)
            {
                _dashboardWindow = new DashboardWindow(
                _tokenUsage,
                _settings,
                _projectBrain,
                () => _projectBrainStatus,
                () => _projectBrainLastError,
                SelectProjectFolder,
                ReindexCurrentProjectAsync,
                ReloadCurrentProjectAsync,
                OpenRag,
                ShowVectorStoreStatus,
                () => OpenProjectBrain(),
                SetPetImage,
                ReapplyBrainSettings,
                SetActiveProjectFolder,
                IndexProjectAtAsync,
                LaunchBrainWeb);
            _dashboardWindow.Closing += (_, args) =>
            {
                if (_isExiting)
                {
                    return;
                }

                args.Cancel = true;
                _dashboardWindow.Hide();
            };
        }

        _dashboardWindow.Show();
        if (_dashboardWindow.WindowState == WindowState.Minimized)
        {
            _dashboardWindow.WindowState = WindowState.Normal;
        }

        _dashboardWindow.Activate();
        _dashboardWindow.Refresh();
    }

    private void OpenRag()
    {
        if (_settings is null || _projectBrain is null)
        {
            return;
        }

        _ragWindow?.Close();
        _ragWindow = new RagWindow(_settings, _projectBrain, ReindexCurrentProjectAsync, () => _projectBrainStatus, () => _projectBrainLastError);
        _ragWindow.Closed += (_, _) => _ragWindow = null;
        _ragWindow.Show();
        _ragWindow.Activate();
    }

    private string ProjectStatusText()
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_settings.ProjectRoot))
        {
            return "Choose a folder to build Woody's local project brain.";
        }

        var status = _projectBrainStatus switch
        {
            "indexing" => "Indexing project brain...",
            "semantic_indexing" => "Creating local ONNX embeddings and saving to the SQLite vector DB...",
            "ready" => "RAG ready. Woody will compile prompts with project context.",
            "partial_ready" => "Partial RAG ready. Some chunks failed, fallback still works.",
            "vector_store_unavailable" => "Local vector store unavailable. Woody will use keyword fallback.",
            "embedding_unavailable" => "Local embedder unavailable. Woody will use keyword fallback.",
            "error" => $"Index error: {_projectBrainLastError}",
            "folder_selected" => "Folder selected. Indexing will start after choosing/re-indexing.",
            _ => "Folder selected."
        };

        return status;
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

    // Project-aware flow: detect IDE workspace, retrieve brain context, rewrite, paste back into the editor.
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

        var ide = ActiveIdeResolver.Resolve(
            context.ProcessName,
            context.WindowTitle,
            _settings.ProjectRoot,
            _projectBrain.ListIndexedProjects());

        var projectRoot = ide.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            if (ActiveIdeResolver.IsIdeProcess(context.ProcessName))
            {
                Notify("Could not detect the Cursor/Codex workspace folder. Focus the IDE window and try again.");
                return;
            }

            projectRoot = _settings.ProjectRoot;
        }

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            Notify("No project folder. Woody will optimize without project context.");
        }
        else
        {
            SetActiveProjectFolder(projectRoot);
            _projectBrainStatus = _projectBrain.IsIndexed(projectRoot)
                ? StatusText(_projectBrain.LoadIndexMetadata(projectRoot)?.Status ?? ProjectBrainStatus.Ready)
                : _projectBrainStatus;
            Notify($"Using brain: {Path.GetFileName(projectRoot)} ({ide.Agent}, {ide.ResolutionSource})");
        }

        if (!string.IsNullOrWhiteSpace(projectRoot) && !_projectBrain.IsIndexed(projectRoot))
        {
            Notify($"Building brain for {Path.GetFileName(projectRoot)}...");
            try
            {
                await _projectBrain.IndexAsync(projectRoot, CancellationToken.None);
                _projectBrainStatus = "ready";
            }
            catch (Exception ex)
            {
                _runtimeStatus?.RecordError(ex);
                Notify("Index failed. Using generic prompt optimization.");
                projectRoot = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            try
            {
                var sync = await _projectBrain.SyncAsync(projectRoot, force: false, CancellationToken.None);
                if (sync.SyncPerformed)
                {
                    Notify($"Brain reloaded: {sync.Message}");
                    _dashboardWindow?.Refresh();
                }
            }
            catch (Exception ex)
            {
                _runtimeStatus?.RecordError(ex);
            }
        }

        _aiPetWindow?.SetThinking(true);
        EnhancementOutcome outcome;
        try
        {
            outcome = await _projectBrain.EnhanceAsync(
                selected,
                projectRoot,
                CancellationToken.None,
                ide.TargetAgent);
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

        outcome.ResolutionSource = ide.ResolutionSource;
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
            async () => { await ReloadCurrentProjectAsync(); },
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

        SetActiveProjectFolder(dialog.SelectedPath);
        await ReindexCurrentProjectAsync();
    }

    private void SetActiveProjectFolder(string projectRoot)
    {
        if (_settings is null || _settingsRepository is null || string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        ActiveProjectStore.Set(projectRoot, _settings, _settingsRepository.Save);
        _projectBrainStatus = _projectBrain?.IsIndexed(_settings.ProjectRoot) == true
            ? StatusText(_projectBrain.LoadIndexMetadata(_settings.ProjectRoot)?.Status ?? ProjectBrainStatus.Ready)
            : "folder_selected";
        _projectBrainLastError = null;
        _dashboardWindow?.Refresh();
    }

    private async Task IndexProjectAtAsync(string projectRoot)
    {
        if (_projectBrain is null || string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        SetActiveProjectFolder(projectRoot);
        _aiPetWindow?.SetThinking(true);
        _projectBrainStatus = "indexing";
        _projectBrainLastError = null;
        _dashboardWindow?.Refresh();
        try
        {
            var report = await _projectBrain.SyncAsync(projectRoot, force: true, CancellationToken.None);
            var metadata = _projectBrain.LoadIndexMetadata(projectRoot);
            _projectBrainStatus = StatusText(metadata?.Status ?? ProjectBrainStatus.Ready);
            _projectBrainLastError = metadata?.LastError;
            Notify(report.Message);
        }
        catch (Exception ex)
        {
            _projectBrainStatus = "error";
            _projectBrainLastError = ex.Message;
            _runtimeStatus?.RecordError(ex);
            Notify("Indexing failed.");
        }
        finally
        {
            _aiPetWindow?.SetThinking(false);
            _dashboardWindow?.Refresh();
            _ragWindow?.Refresh();
        }
    }

    private void LaunchBrainWeb(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            Notify("Select a project folder first.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "woody",
                Arguments = $"brain open --path \"{projectRoot}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _runtimeStatus?.RecordError(ex);
            Notify("Could not launch woody brain. Ensure woody is on PATH.");
        }
    }

    private async Task ReindexCurrentProjectAsync()
    {
        await SyncCurrentProjectAsync(force: true, notifyWhenUnchanged: false);
    }

    private async Task<string> ReloadCurrentProjectAsync() =>
        await SyncCurrentProjectAsync(force: false, notifyWhenUnchanged: true);

    private async Task<string> SyncCurrentProjectAsync(bool force, bool notifyWhenUnchanged)
    {
        if (_projectBrain is null || _settings is null || string.IsNullOrWhiteSpace(_settings.ProjectRoot))
        {
            _projectBrainStatus = "no_folder";
            const string message = "Select a project folder first.";
            Notify(message);
            return message;
        }

        _aiPetWindow?.SetThinking(true);
        _projectBrainStatus = "indexing";
        _projectBrainLastError = null;
        _dashboardWindow?.SetReloadActivity(force ? "Re-indexing project…" : "Scanning project folder for file changes…", true);
        _dashboardWindow?.Refresh();
        _ragWindow?.Refresh();
        try
        {
            var report = await _projectBrain.SyncAsync(_settings.ProjectRoot, force, CancellationToken.None);
            var metadata = _projectBrain.LoadIndexMetadata(_settings.ProjectRoot);
            _projectBrainStatus = StatusText(metadata?.Status ?? ProjectBrainStatus.Ready);
            _projectBrainLastError = metadata?.LastError;
            if (report.SyncPerformed || notifyWhenUnchanged)
            {
                Notify(report.Message);
            }

            return report.Message;
        }
        catch (Exception ex)
        {
            _projectBrainStatus = "error";
            _projectBrainLastError = ex.Message;
            _runtimeStatus?.RecordError(ex);
            var message = force ? "Indexing failed." : "Reload failed.";
            Notify(message);
            return $"{message} {ex.Message}".Trim();
        }
        finally
        {
            _aiPetWindow?.SetThinking(false);
            _dashboardWindow?.Refresh();
            _ragWindow?.Refresh();
        }
    }

    private async Task ShowVectorStoreStatus()
    {
        if (_projectBrain is null || _settings is null)
        {
            return;
        }

        var stats = await _projectBrain.GetVectorStatsAsync(_settings.ProjectRoot, CancellationToken.None);
        if (!stats.IsAvailable)
        {
            Notify($"Local vector store unavailable: {stats.Error}");
            return;
        }

        Notify($"SQLite vector DB OK: {stats.CollectionName}, {stats.VectorCount:N0} vectors, dim {stats.VectorDimension}.");
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

    // Pushes the current (live-edited) settings into the brain so a GUI refresh reflects new config without restart.
    private void ReapplyBrainSettings()
    {
        if (_settings is null || _projectBrain is null)
        {
            return;
        }

        _projectBrain.Reconfigure(BuildBrainOptions(_settings));
    }

    private static ProjectBrainOptions BuildBrainOptions(CorrectionSettings settings) =>
        BrainOptionsFactory.FromSettings(settings);

    private static string StatusText(ProjectBrainStatus status) => status switch
    {
        ProjectBrainStatus.Ready => "ready",
        ProjectBrainStatus.PartialReady => "partial_ready",
        ProjectBrainStatus.VectorStoreUnavailable => "vector_store_unavailable",
        ProjectBrainStatus.EmbeddingUnavailable => "embedding_unavailable",
        ProjectBrainStatus.SemanticIndexing => "semantic_indexing",
        ProjectBrainStatus.QuickIndexing => "indexing",
        ProjectBrainStatus.Error => "error",
        _ => status.ToString().ToLowerInvariant()
    };

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
