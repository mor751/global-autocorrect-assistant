using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autocorrect.Core;
using Autocorrect.Core.Brain;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;

namespace Autocorrect.App;

public partial class DashboardWindow : Window
{
    private readonly TokenUsageStore _store;
    private readonly CorrectionSettings _settings;
    private readonly ProjectBrainService _projectBrain;
    private readonly Func<string> _statusProvider;
    private readonly Func<string?> _lastErrorProvider;
    private readonly Action _chooseFolder;
    private readonly Func<Task> _reindexProject;
    private readonly Action _openRag;
    private readonly Func<Task> _showVectorStoreStatus;
    private readonly Action _openProjectBrain;
    private readonly Action _setPetImage;
    private readonly Action _reapplySettings;
    private VectorStoreStats? _lastVectorStats;
    private RetrievalResponse? _lastRetrieval;
    private ProjectIndexMetadata? _lastMetadata;

    public DashboardWindow(
        TokenUsageStore store,
        CorrectionSettings settings,
        ProjectBrainService projectBrain,
        Func<string> statusProvider,
        Func<string?> lastErrorProvider,
        Action chooseFolder,
        Func<Task> reindexProject,
        Action openRag,
        Func<Task> showVectorStoreStatus,
        Action openProjectBrain,
        Action setPetImage,
        Action reapplySettings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _projectBrain = projectBrain;
        _statusProvider = statusProvider;
        _lastErrorProvider = lastErrorProvider;
        _chooseFolder = chooseFolder;
        _reindexProject = reindexProject;
        _openRag = openRag;
        _showVectorStoreStatus = showVectorStoreStatus;
        _openProjectBrain = openProjectBrain;
        _setPetImage = setPetImage;
        _reapplySettings = reapplySettings;
        Loaded += async (_, _) =>
        {
            ShowPage(DashboardPage, DashboardNav, "RAG Dashboard", "Monitor your Retrieval-Augmented Generation pipeline and knowledge brain.");
            await FullRefreshAsync();
        };
    }

    // Single entry point so any Refresh truly reruns: re-apply settings, reload disk state, re-test live services.
    private async Task FullRefreshAsync()
    {
        try
        {
            _reapplySettings();
        }
        catch (Exception ex)
        {
            DiagnosticsText.Text = $"Settings reapply failed: {ex.Message}";
        }

        Refresh();
        await RefreshAsync();
    }

    public void Refresh()
    {
        var brain = _projectBrain.LoadBrain(_settings.ProjectRoot);
        var metadata = _projectBrain.LoadIndexMetadata(_settings.ProjectRoot);
        _lastMetadata = metadata ?? _lastMetadata;
        var snapshot = _store.Snapshot();
        var status = _lastErrorProvider() is { Length: > 0 } error ? $"{_statusProvider()}: {error}" : _statusProvider();

        ProjectNameText.Text = brain?.ProjectName ?? "No project selected";
        ProjectPathText.Text = string.IsNullOrWhiteSpace(_settings.ProjectRoot) ? "No project selected" : _settings.ProjectRoot;
        ProjectStatusChipText.Text = HumanStatus(status);
        SidebarStatusText.Text = status.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                                 status.Contains("error", StringComparison.OrdinalIgnoreCase)
            ? "System needs attention"
            : "All systems operational";

        QuickBrainStatusText.Text = brain is null ? "No folder" : "Ready";
        QuickBrainStatusText.Foreground = BrushFor(brain is null ? "warning" : "ready");
        SemanticStatusText.Text = metadata?.CurrentRagMode() ?? "No brain";
        SemanticStatusText.Foreground = BrushFor(metadata?.Status.ToString() ?? "warning");
        DeepBrainStatusText.Text = metadata?.DeepBrainStatus.ToString() ?? "Pending";
        GemmaStatusText.Text = $"{_settings.WriterModel} configured";

        ScannedFilesText.Text = (metadata?.TotalFiles ?? brain?.Files.Count ?? 0).ToString("N0");
        IndexedFilesText.Text = (metadata?.IndexedFiles ?? brain?.Files.Count ?? 0).ToString("N0");
        ChunkCountText.Text = metadata is null
            ? (brain?.Files.Sum(f => f.PreviewChunks.Count) ?? 0).ToString("N0")
            : $"{metadata.EmbeddedChunks:N0} / {metadata.TotalChunks:N0}";
        SkippedFilesText.Text = (metadata?.SkippedFiles ?? 0).ToString("N0");
        VectorCountText.Text = (_lastVectorStats?.VectorCount ?? metadata?.EmbeddedChunks ?? 0).ToString("N0");
        LastIndexedText.Text = metadata?.LastIndexedAt?.ToLocalTime().ToString("MMM d, HH:mm") ??
                               brain?.IndexedAt.ToLocalTime().ToString("MMM d, HH:mm") ??
                               "Never";
        CurrentModeText.Text = metadata?.CurrentRagMode() ?? "No brain";
        BuildStatusText.Text = status;
        FilesProcessedText.Text = $"{metadata?.IndexedFiles ?? 0:N0} / {metadata?.TotalFiles ?? 0:N0}";
        ChunksCreatedText.Text = $"{metadata?.TotalChunks ?? 0:N0}";
        VectorsUpsertedText.Text = $"{metadata?.EmbeddedChunks ?? 0:N0}";
        BuildProgressBar.Value = metadata is { TotalChunks: > 0 }
            ? Math.Clamp(metadata.EmbeddedChunks * 100.0 / metadata.TotalChunks, 0, 100)
            : 0;

        RecentPromptsList.ItemsSource = _projectBrain.History.Recent(8).Select(entry => new
        {
            Prompt = Trim(entry.OriginalPrompt, 48),
            Mode = metadata?.CurrentRagMode() ?? "Unknown",
            Latency = "No sample",
            Time = entry.Timestamp.ToLocalTime().ToString("HH:mm")
        }).ToList();
        IndexActivityList.ItemsSource = BuildActivity(metadata, brain).ToList();

        PromptsToPlaceholder(snapshot);
        RefreshBrainPage(brain, metadata);
        RefreshVectorPage(metadata);
    }

    private async Task RefreshAsync()
    {
        var projectRoot = _lastMetadata?.ProjectRoot ?? _settings.ProjectRoot;
        _lastVectorStats = await _projectBrain.GetVectorStatsAsync(projectRoot, CancellationToken.None);
        VectorStoreStatusText.Text = _lastVectorStats.IsAvailable ? "Connected" : $"Unavailable: {_lastVectorStats.Error}";
        VectorStoreStatusText.Foreground = BrushFor(_lastVectorStats.IsAvailable ? "ready" : "error");
        VectorCountText.Text = (_lastVectorStats.VectorCount > 0 ? _lastVectorStats.VectorCount : _lastMetadata?.EmbeddedChunks ?? 0).ToString("N0");
        VectorDbStatusText.Text = _lastVectorStats.IsAvailable ? "SQLite vector DB online" : $"Offline: {_lastVectorStats.Error}";
        VectorDbStatusText.Foreground = BrushFor(_lastVectorStats.IsAvailable ? "ready" : "error");
        VectorFooterText.Text = _lastVectorStats.IsAvailable
            ? $"Collection: {_lastVectorStats.CollectionName} | vectors: {_lastVectorStats.VectorCount:N0}"
            : $"Local vector store unavailable ({Empty(_lastVectorStats.Error)}).";

        var diagnostics = await _projectBrain.TestEmbeddingAsync(CancellationToken.None);
        ApplyEmbeddingDiagnostics(diagnostics);
    }

    private void PromptsToPlaceholder(TokenUsageState snapshot)
    {
        if (!_projectBrain.History.Recent(1).Any())
        {
            RecentPromptsList.ItemsSource = new[] { new { Prompt = "No optimized prompts yet", Mode = "Empty", Latency = "No sample", Time = "" } };
        }
    }

    private void RefreshBrainPage(ProjectBrainData? brain, ProjectIndexMetadata? metadata)
    {
        ExplorerPathText.Text = string.IsNullOrWhiteSpace(_settings.ProjectRoot) ? "No folder selected" : _settings.ProjectRoot;
        ProjectTree.Items.Clear();
        if (brain is null)
        {
            ProjectTree.Items.Add(new TreeViewItem { Header = "No indexed project yet" });
            KnowledgeGraphItems.ItemsSource = new[] { new { Label = "No project brain", Meta = "Choose a folder and index it first." } };
            BrainRetrievalModeText.Text = "No brain";
            TopLinkedFilesText.Text = "No retrieval yet";
            BrainStatusText.Text = "No folder";
            BrainVectorCountText.Text = "0";
            return;
        }

        RenderFolderTree(brain);
        KnowledgeGraphItems.ItemsSource = brain.Files
            .OrderByDescending(file => file.Importance)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Take(28)
            .Select(file => new { Label = Path.GetFileName(file.Path), Meta = $"{file.Role} | {file.Path}" })
            .ToList();
        BrainRetrievalModeText.Text = metadata?.CurrentRagMode() ?? "Keyword fallback";
        TopLinkedFilesText.Text = _lastRetrieval?.Results.Count > 0
            ? string.Join(", ", _lastRetrieval.Results.Take(3).Select(r => r.FilePath))
            : "Run a prompt/search";
        BrainStatusText.Text = metadata?.Status.ToString() ?? "Ready";
        BrainVectorCountText.Text = (_lastVectorStats?.VectorCount ?? metadata?.EmbeddedChunks ?? 0).ToString("N0");
    }

    private void RefreshVectorPage(ProjectIndexMetadata? metadata)
    {
        VectorStorageText.Text = "SQLite (local)";
        CollectionText.Text = metadata?.Collection ?? "No collection";
        EmbeddingModelText.Text = "BAAI/bge-small-en-v1.5 (ONNX, in-process)";
        VectorDimensionText.Text = metadata?.VectorDimension > 0 ? metadata.VectorDimension.ToString() : "384 expected";
        VectorTotalsText.Text = $"{_lastVectorStats?.VectorCount ?? metadata?.EmbeddedChunks ?? 0:N0} / {metadata?.TotalChunks ?? 0:N0}";
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        await FullRefreshAsync();
    }

    private void ChooseFolder_OnClick(object sender, RoutedEventArgs e) => _chooseFolder();

    private async void Reindex_OnClick(object sender, RoutedEventArgs e)
    {
        await _reindexProject();
        await FullRefreshAsync();
    }

    private void OpenRag_OnClick(object sender, RoutedEventArgs e) => ShowPage(BrainPage, BrainNav, "Woody Brain", "Visualize your project knowledge brain and compile better prompts.");

    private async void VectorStoreStatus_OnClick(object sender, RoutedEventArgs e)
    {
        await TestVectorStoreAsync();
    }

    private void ProjectBrain_OnClick(object sender, RoutedEventArgs e) => ShowPage(BrainPage, BrainNav, "Woody Brain", "Visualize your project knowledge brain and compile better prompts.");

    private void SetPetImage_OnClick(object sender, RoutedEventArgs e) => _setPetImage();

    private void DashboardNav_OnClick(object sender, RoutedEventArgs e) => ShowPage(DashboardPage, DashboardNav, "RAG Dashboard", "Monitor your Retrieval-Augmented Generation pipeline and knowledge brain.");

    private void BrainNav_OnClick(object sender, RoutedEventArgs e) => ShowPage(BrainPage, BrainNav, "Woody Brain", "Visualize your project knowledge brain and compile better prompts.");

    private void VectorNav_OnClick(object sender, RoutedEventArgs e) => ShowPage(VectorPage, VectorNav, "Vector DB & Retrieval", "Explore your vector database, search chunks, and inspect embeddings.");

    private void HistoryNav_OnClick(object sender, RoutedEventArgs e) => ShowPlaceholder("Prompt History", "Recent optimized prompts appear on the dashboard. A full history table will live here.");

    private void ProjectsNav_OnClick(object sender, RoutedEventArgs e) => ShowPlaceholder("Projects", "Choose a project folder from the dashboard or Woody Brain page.");

    private void SettingsNav_OnClick(object sender, RoutedEventArgs e) => ShowPlaceholder("Settings", $"Embedder: bge-small ONNX (in-process)\nVector DB: SQLite (local)\nWriter: {_settings.WriterModel}");

    private async void TestEmbedder_OnClick(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.Text = "Testing local embedder (this downloads the model on first run)...";
        var diagnostics = await _projectBrain.TestEmbeddingAsync(CancellationToken.None);
        ApplyEmbeddingDiagnostics(diagnostics);
    }

    private async void TestVectorStore_OnClick(object sender, RoutedEventArgs e)
    {
        await TestVectorStoreAsync();
    }

    private async Task TestVectorStoreAsync()
    {
        DiagnosticsText.Text = "Testing local SQLite vector store...";
        _lastVectorStats = await _projectBrain.GetVectorStatsAsync(_lastMetadata?.ProjectRoot ?? _settings.ProjectRoot, CancellationToken.None);
        DiagnosticsText.Text = _lastVectorStats.IsAvailable
            ? $"SQLite vector DB online. Collection: {_lastVectorStats.CollectionName}. Vectors: {_lastVectorStats.VectorCount:N0}. Dimension: {_lastVectorStats.VectorDimension}."
            : $"Vector DB: SQLite (local)\n" +
              $"Status: unavailable\n" +
              $"Error: {Empty(_lastVectorStats.Error)}";
        Refresh();
    }

    private async void FixPrompt_OnClick(object sender, RoutedEventArgs e)
    {
        var prompt = OriginalPromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            CompilerModeText.Text = "Write a prompt first.";
            return;
        }

        CompilerModeText.Text = "Compiling prompt...";
        var outcome = await _projectBrain.EnhanceAsync(prompt, _settings.ProjectRoot, CancellationToken.None);
        OptimizedPromptBox.Text = outcome.Result.ImprovedPrompt;
        _lastRetrieval = await _projectBrain.SearchDetailedAsync(prompt, _settings.ProjectRoot, _settings.RetrievalTopK, CancellationToken.None);
        CompilerModeText.Text = $"{_lastRetrieval.RetrievalMode} | {(outcome.Result.UsedOllama ? "Gemma3 writer" : "deterministic fallback")}";
        CompilerFilesList.ItemsSource = _lastRetrieval.Results.Select(r => new
        {
            File = r.FilePath,
            Score = r.Score.ToString("0.000"),
            Lines = $"{r.StartLine}-{r.EndLine}"
        }).ToList();
        Refresh();
    }

    private void CopyPrompt_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(OptimizedPromptBox.Text))
        {
            WpfClipboard.SetText(OptimizedPromptBox.Text);
            CompilerModeText.Text = "Optimized prompt copied.";
        }
    }

    private async void VectorSearch_OnClick(object sender, RoutedEventArgs e)
    {
        var topK = TopKBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var parsed)
            ? parsed
            : 10;
        _lastRetrieval = await _projectBrain.SearchDetailedAsync(VectorQueryBox.Text, _settings.ProjectRoot, topK, CancellationToken.None);
        VectorResultsList.ItemsSource = _lastRetrieval.Results.Select(r => new VectorRow(r)).ToList();
        if (_lastRetrieval.Results.Count == 0)
        {
            SelectedChunkMetaText.Text = "No retrieval results yet.";
            SelectedChunkPreviewBox.Text = string.Empty;
        }
        Refresh();
    }

    private void VectorResultsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VectorResultsList.SelectedItem is not VectorRow row)
        {
            return;
        }

        SelectedChunkMetaText.Text =
            $"File: {row.Result.FilePath}\nSymbol: {row.Result.Symbol}\nType: {row.Result.ChunkType}\nLines: {row.Result.StartLine}-{row.Result.EndLine}\nScore: {row.Result.Score:0.000}\nMetadata: {string.Join(", ", row.Result.Metadata.Take(8).Select(p => $"{p.Key}={p.Value}"))}";
        SelectedChunkPreviewBox.Text = string.IsNullOrWhiteSpace(row.Result.Content)
            ? row.Result.ContentPreview
            : row.Result.Content;
    }

    private void BrainMap_OnClick(object sender, RoutedEventArgs e)
    {
        new VectorBrainWindow(_settings, _projectBrain) { Owner = this }.Show();
    }

    private void OpenExplorer_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ProjectRoot) && Directory.Exists(_settings.ProjectRoot))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.ProjectRoot}\"") { UseShellExecute = true });
        }
    }

    private void OpenSkippedReport_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var path = _projectBrain.SkippedReportPath(_settings.ProjectRoot);
        if (path is null || !File.Exists(path))
        {
            DiagnosticsText.Text = "No skipped-files report yet. Re-index the project first.";
            return;
        }

        var skipped = _projectBrain.LoadSkippedReport(_settings.ProjectRoot);
        DiagnosticsText.Text = $"{skipped.Count} files skipped (report: {path})\n\n" +
            string.Join("\n", skipped.Take(60).Select(item => $"{item.Path}  -  {item.Reason}"));
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void ShowPlaceholder(string title, string text)
    {
        PlaceholderTitle.Text = title;
        PlaceholderText.Text = text;
        ShowPage(PlaceholderPage, title switch
        {
            "Prompt History" => HistoryNav,
            "Projects" => ProjectsNav,
            "Settings" => SettingsNav,
            _ => DashboardNav
        }, title, text);
    }

    private void ShowPage(UIElement page, System.Windows.Controls.Button nav, string title, string subtitle)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        BrainPage.Visibility = Visibility.Collapsed;
        VectorPage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        HeaderTitle.Text = title;
        HeaderSubtitle.Text = subtitle;

        foreach (var button in new[] { DashboardNav, BrainNav, VectorNav, HistoryNav, ProjectsNav, SettingsNav })
        {
            button.Background = WpfBrushes.Transparent;
            button.BorderBrush = WpfBrushes.Transparent;
            button.Foreground = new SolidColorBrush(WpfColor.FromRgb(202, 211, 223));
        }

        nav.Background = new SolidColorBrush(WpfColor.FromRgb(31, 49, 78));
        nav.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(78, 121, 214));
        nav.Foreground = WpfBrushes.White;
    }

    private void ApplyEmbeddingDiagnostics(EmbeddingDiagnostics diagnostics)
    {
        EmbedderStatusText.Text = diagnostics.Available
            ? $"Ready ({diagnostics.VectorDimension} dim)"
            : diagnostics.Summary();
        EmbedderStatusText.Foreground = BrushFor(diagnostics.Available ? "ready" : "error");
        DiagnosticsText.Text =
            $"Local embedder (in-process ONNX)\n" +
            $"Available: {(diagnostics.Available ? "yes" : "no")}\n" +
            $"Model downloaded: {(diagnostics.ModelDownloaded ? "yes" : "no")}\n" +
            $"Model: {diagnostics.ModelName}\n" +
            $"Model path: {Empty(diagnostics.ModelPath)}\n" +
            $"Dimension: {diagnostics.VectorDimension} (expected 384)\n" +
            $"Last check: {diagnostics.LastHealthCheck.ToLocalTime():HH:mm:ss}\n" +
            $"Last error: {Empty(diagnostics.LastError)}";

        if (!diagnostics.Available && !diagnostics.ModelDownloaded)
        {
            DiagnosticsText.Text +=
                "\n\nThe embedding model downloads automatically on first use (~130 MB). " +
                "Ensure the machine has internet access for that one-time download.";
        }
    }

    private void RenderFolderTree(ProjectBrainData brain)
    {
        var root = new TreeViewItem { Header = $"{brain.ProjectName} ({brain.Files.Count} files)", IsExpanded = true };
        ProjectTree.Items.Add(root);
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase) { [""] = root };
        foreach (var file in brain.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var parts = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentKey = string.Empty;
            var parent = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                currentKey = currentKey.Length == 0 ? parts[i] : $"{currentKey}/{parts[i]}";
                if (!folders.TryGetValue(currentKey, out var folder))
                {
                    folder = new TreeViewItem { Header = parts[i], IsExpanded = folders.Count < 12 };
                    parent.Items.Add(folder);
                    folders[currentKey] = folder;
                }

                parent = folder;
            }

            parent.Items.Add(new TreeViewItem { Header = $"{IconFor(file.Extension)} {parts.Last()}" });
        }
    }

    private static IEnumerable<object> BuildActivity(ProjectIndexMetadata? metadata, ProjectBrainData? brain)
    {
        if (metadata is null)
        {
            yield return new { Activity = "No project indexed yet", Time = "" };
            yield break;
        }

        yield return new { Activity = $"Scanned {metadata.TotalFiles:N0} files", Time = metadata.LastIndexedAt?.ToLocalTime().ToString("HH:mm") ?? "" };
        yield return new { Activity = $"Created {metadata.TotalChunks:N0} chunks", Time = metadata.LastIndexedAt?.ToLocalTime().ToString("HH:mm") ?? "" };
        yield return new { Activity = $"Upserted {metadata.EmbeddedChunks:N0} vectors", Time = metadata.LastIndexedAt?.ToLocalTime().ToString("HH:mm") ?? "" };
        if (metadata.SkippedFiles > 0)
        {
            yield return new { Activity = $"Skipped {metadata.SkippedFiles:N0} files", Time = metadata.LastIndexedAt?.ToLocalTime().ToString("HH:mm") ?? "" };
        }

        if (metadata.FailedChunks > 0)
        {
            yield return new { Activity = $"Failed chunks: {metadata.FailedChunks:N0}", Time = metadata.LastIndexedAt?.ToLocalTime().ToString("HH:mm") ?? "" };
        }

        foreach (var file in brain?.Files.Take(5) ?? Enumerable.Empty<ProjectFileSummary>())
        {
            yield return new { Activity = $"Chunked file: {file.Path}", Time = "" };
        }
    }

    private static System.Windows.Media.Brush BrushFor(string status)
    {
        if (status.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("healthy", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(WpfColor.FromRgb(88, 210, 107));
        }

        if (status.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(WpfColor.FromRgb(248, 113, 113));
        }

        return new SolidColorBrush(WpfColor.FromRgb(247, 201, 72));
    }

    private static string HumanStatus(string status) => string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Replace('_', ' ');

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    private static string Empty(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value;

    private static string IconFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".ts" or ".tsx" => "TS",
        ".js" or ".jsx" => "JS",
        ".md" or ".mdx" => "MD",
        ".json" => "JSON",
        ".sql" => "SQL",
        ".cs" => "CS",
        _ => "FILE"
    };

    private sealed class VectorRow
    {
        public VectorRow(RetrievalResult result)
        {
            Result = result;
        }

        public RetrievalResult Result { get; }
        public string Score => Result.Score.ToString("0.000");
        public string FilePath => Result.FilePath;
        public string ChunkType => Result.ChunkType;
        public string Symbol => string.IsNullOrWhiteSpace(Result.Symbol) ? "-" : Result.Symbol;
        public string Lines => $"{Result.StartLine}-{Result.EndLine}";
        public string Reason => Result.Reason;
    }
}
