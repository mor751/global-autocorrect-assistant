using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autocorrect.Core;
using Autocorrect.Core.Brain;
using WpfPoint = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace Autocorrect.App;

public partial class VectorBrainWindow : Window
{
    private readonly CorrectionSettings _settings;
    private readonly ProjectBrainService _projectBrain;
    private readonly Dictionary<Ellipse, VectorPoint> _nodes = new();
    private bool _isPanning;
    private WpfPoint _panStart;
    private double _panOriginX;
    private double _panOriginY;
    private Ellipse? _selected;

    public VectorBrainWindow(CorrectionSettings settings, ProjectBrainService projectBrain)
    {
        InitializeComponent();
        _settings = settings;
        _projectBrain = projectBrain;
        Loaded += async (_, _) => await BuildAsync();
    }

    // Loads all vectors, projects them to 2D off the UI thread, then renders the network.
    private async Task BuildAsync()
    {
        LoadingText.Text = "Loading vectors...";
        LoadingText.Visibility = Visibility.Visible;
        MapCanvas.Children.Clear();
        _nodes.Clear();
        _selected = null;

        var points = await _projectBrain.ExportVectorsAsync(_settings.ProjectRoot, CancellationToken.None);
        if (points.Count == 0)
        {
            LoadingText.Text = "No vectors found. Select and index a project folder first.";
            StatsText.Text = string.Empty;
            return;
        }

        LoadingText.Text = $"Projecting {points.Count:N0} vectors...";
        var layout = await Task.Run(() => VectorBrainLayout.Compute(points));
        Render(points, layout);
        LoadingText.Visibility = Visibility.Collapsed;

        StatsText.Text = $"{points.Count:N0} vectors\n{layout.Edges.Count:N0} neighbor links\ndim {points[0].Vector.Length}";
        SubtitleText.Text = $"Each dot is one embedded code chunk; lines connect nearest neighbors in meaning.";
    }

    private void Render(IReadOnlyList<VectorPoint> points, VectorLayoutResult layout)
    {
        var width = Math.Max(MapCanvas.ActualWidth, 640);
        var height = Math.Max(MapCanvas.ActualHeight, 520);
        const double margin = 44;

        var colors = BuildFolderColors(points);
        var edgeBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3B, 0x52)) { Opacity = 0.45 };

        double MapX(int i) => margin + layout.X[i] * (width - 2 * margin);
        double MapY(int i) => margin + layout.Y[i] * (height - 2 * margin);

        foreach (var (a, b) in layout.Edges)
        {
            MapCanvas.Children.Add(new Line
            {
                X1 = MapX(a),
                Y1 = MapY(a),
                X2 = MapX(b),
                Y2 = MapY(b),
                Stroke = edgeBrush,
                StrokeThickness = 0.7
            });
        }

        for (var i = 0; i < points.Count; i++)
        {
            var folder = FolderKey(points[i]);
            var fill = colors.TryGetValue(folder, out var color) ? color : Colors.Gray;
            var node = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                StrokeThickness = 0.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = points[i].FilePath
            };

            Canvas.SetLeft(node, MapX(i) - node.Width / 2);
            Canvas.SetTop(node, MapY(i) - node.Height / 2);
            node.MouseLeftButtonDown += Node_OnClick;
            _nodes[node] = points[i];
            MapCanvas.Children.Add(node);
        }

        BuildLegend(points, colors);
    }

    private void Node_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Ellipse node || !_nodes.TryGetValue(node, out var point))
        {
            return;
        }

        e.Handled = true;
        if (_selected is not null)
        {
            _selected.Width = 9;
            _selected.Height = 9;
            _selected.StrokeThickness = 0.5;
        }

        node.Width = 16;
        node.Height = 16;
        node.StrokeThickness = 2;
        node.Stroke = new SolidColorBrush(Colors.White);
        Canvas.SetLeft(node, Canvas.GetLeft(node) - 3.5);
        Canvas.SetTop(node, Canvas.GetTop(node) - 3.5);
        _selected = node;

        SelectedFileText.Text = point.FilePath;
        var symbol = string.IsNullOrWhiteSpace(point.Symbol) ? "—" : point.Symbol;
        SelectedMetaText.Text = $"{point.ChunkType} · {symbol}\nlines {point.StartLine}-{point.EndLine}\nfolder: {FolderKey(point)}";
    }

    private static Dictionary<string, Color> BuildFolderColors(IReadOnlyList<VectorPoint> points)
    {
        var ordered = points
            .GroupBy(FolderKey)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .ToList();

        var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Count; i++)
        {
            colors[ordered[i]] = FromHue(i * 360.0 / Math.Max(1, ordered.Count));
        }

        return colors;
    }

    private void BuildLegend(IReadOnlyList<VectorPoint> points, Dictionary<string, Color> colors)
    {
        LegendItems.ItemsSource = points
            .GroupBy(FolderKey)
            .OrderByDescending(group => group.Count())
            .Take(10)
            .Select(group => new
            {
                Label = $"{group.Key} ({group.Count()})",
                Color = new SolidColorBrush(colors.TryGetValue(group.Key, out var color) ? color : Colors.Gray)
            })
            .ToList();
    }

    private static string FolderKey(VectorPoint point)
    {
        var path = point.FilePath.Replace('\\', '/');
        var slash = path.IndexOf('/');
        return slash <= 0 ? "(root)" : path[..slash];
    }

    private static Color FromHue(double hue)
    {
        var h = hue / 60.0;
        var x = 1 - Math.Abs(h % 2 - 1);
        double r = 0, g = 0, b = 0;
        switch ((int)h % 6)
        {
            case 0: r = 1; g = x; break;
            case 1: r = x; g = 1; break;
            case 2: g = 1; b = x; break;
            case 3: g = x; b = 1; break;
            case 4: r = x; b = 1; break;
            default: r = 1; b = x; break;
        }

        return Color.FromRgb((byte)(r * 210 + 30), (byte)(g * 210 + 30), (byte)(b * 210 + 30));
    }

    private async void Reload_OnClick(object sender, RoutedEventArgs e) => await BuildAsync();

    private void ResetView_OnClick(object sender, RoutedEventArgs e)
    {
        MapScale.ScaleX = 1;
        MapScale.ScaleY = 1;
        MapTranslate.X = 0;
        MapTranslate.Y = 0;
    }

    private void MapCanvas_OnMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        var newScale = Math.Clamp(MapScale.ScaleX * factor, 0.2, 8.0);
        factor = newScale / MapScale.ScaleX;

        var pos = e.GetPosition((UIElement)MapCanvas.Parent);
        MapTranslate.X = pos.X - (pos.X - MapTranslate.X) * factor;
        MapTranslate.Y = pos.Y - (pos.Y - MapTranslate.Y) * factor;
        MapScale.ScaleX = newScale;
        MapScale.ScaleY = newScale;
    }

    private void MapCanvas_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition((UIElement)MapCanvas.Parent);
        _panOriginX = MapTranslate.X;
        _panOriginY = MapTranslate.Y;
        MapCanvas.CaptureMouse();
    }

    private void MapCanvas_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var pos = e.GetPosition((UIElement)MapCanvas.Parent);
        MapTranslate.X = _panOriginX + (pos.X - _panStart.X);
        MapTranslate.Y = _panOriginY + (pos.Y - _panStart.Y);
    }

    private void MapCanvas_OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isPanning = false;
        MapCanvas.ReleaseMouseCapture();
    }
}
