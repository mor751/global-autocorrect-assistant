using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autocorrect.Core.Brain;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Autocorrect.App;

public partial class ProjectBrainWindow : Window
{
    private const double CanvasWidth = 1100;
    private const double CanvasHeight = 760;

    private static readonly (string Name, FileRole[] Roles)[] Categories =
    {
        ("UI", new[] { FileRole.Component, FileRole.Route }),
        ("Styles", new[] { FileRole.Style }),
        ("Logic", new[] { FileRole.Util, FileRole.Hook }),
        ("API", new[] { FileRole.Api }),
        ("Data", new[] { FileRole.Database }),
        ("Docs", new[] { FileRole.Docs })
    };

    private readonly ProjectBrainData _brain;
    private readonly HashSet<string> _highlight;
    private readonly Func<Task> _reindexAction;

    public ProjectBrainWindow(ProjectBrainData brain, IEnumerable<string> highlightPaths, Func<Task> reindexAction)
    {
        InitializeComponent();
        _brain = brain;
        _highlight = highlightPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _reindexAction = reindexAction;
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        TitleText.Text = _brain.ProjectName;
        var stack = string.Join(" · ", _brain.Stack.Describe());
        SubtitleText.Text = $"{_brain.Files.Count} files indexed · {stack} · updated {_brain.IndexedAt.LocalDateTime:MMM d HH:mm}";
        LegendText.Text = _highlight.Count > 0 ? $"{_highlight.Count} files highlighted for the last prompt" : "Cyan = highlighted by prompt analysis";
        DrawGraph();
    }

    // Draws a radial neural tree: project root, category branches, and file leaves (highlighting relevant ones).
    private void DrawGraph()
    {
        GraphCanvas.Children.Clear();
        var center = new Point(CanvasWidth / 2, CanvasHeight / 2);

        var active = Categories
            .Select(c => (c.Name, Files: _brain.Files.Where(f => c.Roles.Contains(f.Role)).Take(12).ToList()))
            .Where(c => c.Files.Count > 0)
            .ToList();

        if (active.Count == 0)
        {
            DrawNode(center, "No indexed files", "#22D3EE", 64, true);
            return;
        }

        var branchRadius = 220.0;
        for (var i = 0; i < active.Count; i++)
        {
            var angle = 2 * Math.PI * i / active.Count - Math.PI / 2;
            var branch = new Point(center.X + branchRadius * Math.Cos(angle), center.Y + branchRadius * Math.Sin(angle));
            DrawEdge(center, branch, "#16313D");
            DrawNode(branch, $"{active[i].Name} ({active[i].Files.Count})", "#3FA9C4", 30, false);
            DrawLeaves(branch, angle, active[i].Files);
        }

        DrawEdgeless();
        DrawNode(center, _brain.ProjectName, "#22D3EE", 50, true);
    }

    private void DrawLeaves(Point branch, double angle, IReadOnlyList<ProjectFileSummary> files)
    {
        var leafRadius = 150.0;
        var spread = Math.PI / 3.2;
        for (var j = 0; j < files.Count; j++)
        {
            var offset = files.Count == 1 ? 0 : spread * (j / (double)(files.Count - 1) - 0.5);
            var leafAngle = angle + offset;
            var leaf = new Point(branch.X + leafRadius * Math.Cos(leafAngle), branch.Y + leafRadius * Math.Sin(leafAngle));
            var highlighted = _highlight.Contains(files[j].Path);
            DrawEdge(branch, leaf, highlighted ? "#22D3EE" : "#13242E");
            DrawNode(leaf, System.IO.Path.GetFileName(files[j].Path), highlighted ? "#67E8F9" : "#24414F", highlighted ? 16 : 11, highlighted);
        }
    }

    private void DrawEdge(Point from, Point to, string color)
    {
        GraphCanvas.Children.Add(new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = Brush(color),
            StrokeThickness = 1.4
        });
    }

    private void DrawEdgeless()
    {
        // Reserved hook for future edge styles (imports/renders). TODO: render import edges from brain.Graph.
    }

    private void DrawNode(Point at, string label, string color, double diameter, bool glow)
    {
        var ellipse = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = Brush(color),
            Stroke = Brush("#0A1015"),
            StrokeThickness = 2
        };

        if (glow)
        {
            ellipse.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#22D3EE"),
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.85
            };
        }

        Canvas.SetLeft(ellipse, at.X - diameter / 2);
        Canvas.SetTop(ellipse, at.Y - diameter / 2);
        GraphCanvas.Children.Add(ellipse);

        var text = new TextBlock
        {
            Text = label,
            Foreground = Brush("#DCEFF6"),
            FontSize = glow ? 13 : 11,
            TextAlignment = TextAlignment.Center,
            Width = 150,
            TextWrapping = TextWrapping.Wrap
        };

        Canvas.SetLeft(text, at.X - 75);
        Canvas.SetTop(text, at.Y + diameter / 2 + 2);
        GraphCanvas.Children.Add(text);
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private async void Reindex_OnClick(object sender, RoutedEventArgs e)
    {
        ReindexButton.Content = "Re-indexing…";
        ReindexButton.IsEnabled = false;
        await _reindexAction();
        ReindexButton.Content = "Re-index";
        ReindexButton.IsEnabled = true;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
