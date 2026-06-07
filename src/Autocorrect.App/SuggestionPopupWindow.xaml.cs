using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autocorrect.Core;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;

namespace Autocorrect.App;

public partial class SuggestionPopupWindow : Window
{
    public event EventHandler<string>? SuggestionAccepted;
    private IReadOnlyList<WordSuggestion> _suggestions = Array.Empty<WordSuggestion>();
    private int _selectedIndex;

    public bool IsManuallyPositioned { get; private set; }

    public long ManualPlacementClickVersion { get; private set; } = -1;

    public SuggestionPopupWindow()
    {
        InitializeComponent();
    }

    public void ShowSuggestions(IReadOnlyList<WordSuggestion> suggestions, double left, double top, bool keepManualPosition)
    {
        _suggestions = suggestions;
        _selectedIndex = 0;
        SuggestionItems.ItemsSource = suggestions;
        if (!keepManualPosition)
        {
            Left = left;
            Top = top;
        }

        if (!IsVisible)
        {
            Show();
        }

        Dispatcher.BeginInvoke(UpdateSelectionBrushes);
    }

    public void MoveSelection(int delta)
    {
        if (_suggestions.Count == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + delta + _suggestions.Count) % _suggestions.Count;
        UpdateSelectionBrushes();
    }

    public bool AcceptSelected()
    {
        if (_suggestions.Count == 0)
        {
            return false;
        }

        SuggestionAccepted?.Invoke(this, _suggestions[_selectedIndex].Text);
        return true;
    }

    public void HideSuggestions()
    {
        Hide();
    }

    public void ClearManualPlacement()
    {
        IsManuallyPositioned = false;
        ManualPlacementClickVersion = -1;
    }

    private void Suggestion_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string suggestion })
        {
            SuggestionAccepted?.Invoke(this, suggestion);
        }
    }

    private void SuggestionButton_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button)
        {
            ApplySelectionBrush(button);
        }
    }

    private void SuggestionHeader_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
            IsManuallyPositioned = true;
            ManualPlacementClickVersion = InputAnchorTracker.CurrentClickVersion;
        }
        catch
        {
            // DragMove can throw if the mouse is released during the call.
        }
    }

    private void UpdateSelectionBrushes()
    {
        foreach (var button in FindVisualChildren<WpfButton>(SuggestionItems))
        {
            ApplySelectionBrush(button);
        }
    }

    private void ApplySelectionBrush(WpfButton button)
    {
        if (button.Tag is not string text)
        {
            return;
        }

        var isSelected = _suggestions.Count > _selectedIndex &&
                         string.Equals(_suggestions[_selectedIndex].Text, text, StringComparison.OrdinalIgnoreCase);
        button.Background = new SolidColorBrush(
            WpfColor.FromRgb(
                isSelected ? (byte)37 : (byte)31,
                isSelected ? (byte)99 : (byte)41,
                isSelected ? (byte)235 : (byte)55));
        button.BorderBrush = new SolidColorBrush(
            isSelected ? WpfColor.FromRgb(96, 165, 250) : WpfColor.FromRgb(51, 65, 85));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
