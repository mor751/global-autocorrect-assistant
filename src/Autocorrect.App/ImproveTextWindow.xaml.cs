using System.Windows;
using Autocorrect.Core;

namespace Autocorrect.App;

public partial class ImproveTextWindow : Window
{
    private readonly IAiRewriteService _rewriteService;
    private readonly CorrectionSettings _settings;
    private readonly AppContextSnapshot _context;

    public string? ReplacementText { get; private set; }

    public ImproveTextWindow(
        string originalText,
        IAiRewriteService rewriteService,
        CorrectionSettings settings,
        AppContextSnapshot context)
    {
        InitializeComponent();
        _rewriteService = rewriteService;
        _settings = settings;
        _context = context;
        OriginalTextBox.Text = originalText;
    }

    private async void Action_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !Enum.TryParse<AiRewriteAction>(tag, out var action))
        {
            return;
        }

        var request = new AiRewriteRequest(OriginalTextBox.Text, action, _context, string.Empty, string.Empty);
        var result = await _rewriteService.RewriteAsync(request, _settings, CancellationToken.None);
        if (result is null)
        {
            ExplanationText.Text = "AI rewrite is disabled or blocked for this context.";
            return;
        }

        ResultTextBox.Text = result.RewrittenText;
        ExplanationText.Text = $"{result.ExplanationShort} Token reduction: {result.EstimatedTokenReductionPercent}%";
    }

    private void Replace_OnClick(object sender, RoutedEventArgs e)
    {
        ReplacementText = ResultTextBox.Text;
        DialogResult = true;
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ResultTextBox.Text))
        {
            System.Windows.Clipboard.SetText(ResultTextBox.Text);
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
