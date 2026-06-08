using System.Windows;
using System.Windows.Controls;
using Autocorrect.Core;
using Autocorrect.Core.Brain;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Autocorrect.App;

public partial class RagWindow : Window
{
    private readonly CorrectionSettings _settings;
    private readonly ProjectBrainService _projectBrain;
    private readonly Func<Task> _reindexAction;
    private readonly Func<string> _statusProvider;
    private readonly Func<string?> _lastErrorProvider;

    public RagWindow(
        CorrectionSettings settings,
        ProjectBrainService projectBrain,
        Func<Task> reindexAction,
        Func<string> statusProvider,
        Func<string?> lastErrorProvider)
    {
        InitializeComponent();
        _settings = settings;
        _projectBrain = projectBrain;
        _reindexAction = reindexAction;
        _statusProvider = statusProvider;
        _lastErrorProvider = lastErrorProvider;
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var brain = _projectBrain.LoadBrain(_settings.ProjectRoot);
        var metadata = _projectBrain.LoadIndexMetadata(_settings.ProjectRoot);
        FolderText.Text = string.IsNullOrWhiteSpace(_settings.ProjectRoot)
            ? "No project folder selected"
            : _settings.ProjectRoot;
        StatusText.Text = _lastErrorProvider() is { Length: > 0 } error ? $"{_statusProvider()}: {error}" : _statusProvider();
        VectorDbText.Text = metadata is null
            ? $"{_settings.QdrantUrl} / not indexed"
            : $"{metadata.QdrantCollection} / {metadata.CurrentRagMode()} / dim {metadata.VectorDimension}";

        if (brain is null)
        {
            FileCountText.Text = "0";
            ChunkCountText.Text = "0";
            StackText.Text = "unknown";
            IndexedText.Text = "never";
            VectorDbText.Text = $"{_settings.QdrantUrl} / no brain";
            FilesList.ItemsSource = Array.Empty<object>();
            FolderTree.Items.Clear();
            ResultsList.ItemsSource = Array.Empty<object>();
            return;
        }

        FileCountText.Text = brain.Files.Count.ToString("N0");
        ChunkCountText.Text = brain.Files.Sum(file => file.PreviewChunks.Count).ToString("N0");
        StackText.Text = string.Join(" / ", brain.Stack.Describe().DefaultIfEmpty("unknown"));
        IndexedText.Text = brain.IndexedAt.ToLocalTime().ToString("MMM d, HH:mm");
        FilesList.ItemsSource = brain.Files
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(file => new
            {
                file.Path,
                Meta = $"{file.Role} / {file.Language} / {file.PreviewChunks.Count} preview chunks / {file.SizeBytes / 1024.0:0.0} KB"
            })
            .ToList();
        RenderFolderTree(brain);
    }

    private async void Search_OnClick(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void SearchBox_OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Enter)
        {
            e.Handled = true;
            await RunSearchAsync();
        }
    }

    private async Task RunSearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ResultsList.ItemsSource = Array.Empty<object>();
            return;
        }

        ResultsList.ItemsSource = new[] { new { Path = "Searching...", Reason = string.Empty, Summary = string.Empty } };
        try
        {
            var response = await _projectBrain.SearchDetailedAsync(query, _settings.ProjectRoot, 8, CancellationToken.None);
            ResultsList.ItemsSource = response.Results.Count == 0
                ? new[] { new { Path = "No exact relevant files found", Reason = $"Start by searching for: {query}", Summary = string.Empty } }
                : response.Results.Select(result => new
                {
                    Path = string.IsNullOrWhiteSpace(result.Symbol)
                        ? result.FilePath
                        : $"{result.FilePath} :: {result.Symbol}",
                    Reason = $"{response.RetrievalMode} / {result.Reason} / score {result.Score:0.00} / lines {result.StartLine}-{result.EndLine}",
                    Summary = string.IsNullOrWhiteSpace(result.ContentPreview)
                        ? result.Content[..Math.Min(result.Content.Length, 220)]
                        : result.ContentPreview
                }).ToList();
        }
        catch (Exception ex)
        {
            ResultsList.ItemsSource = new[] { new { Path = "Search failed", Reason = ex.Message, Summary = string.Empty } };
        }
    }

    private async void Reindex_OnClick(object sender, RoutedEventArgs e)
    {
        await _reindexAction();
        Refresh();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => Refresh();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void RenderFolderTree(ProjectBrainData brain)
    {
        FolderTree.Items.Clear();
        var root = new TreeViewItem
        {
            Header = $"{brain.ProjectName} ({brain.Files.Count} files)",
            IsExpanded = true
        };
        FolderTree.Items.Add(root);

        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = root
        };

        foreach (var file in brain.Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            var parts = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentKey = string.Empty;
            var parent = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                currentKey = currentKey.Length == 0 ? parts[i] : $"{currentKey}/{parts[i]}";
                if (!folders.TryGetValue(currentKey, out var folder))
                {
                    folder = new TreeViewItem { Header = parts[i], IsExpanded = folders.Count < 20 };
                    parent.Items.Add(folder);
                    folders[currentKey] = folder;
                }

                parent = folder;
            }

            parent.Items.Add(new TreeViewItem
            {
                Header = $"{parts.Last()} ({file.PreviewChunks.Count})"
            });
        }
    }
}
