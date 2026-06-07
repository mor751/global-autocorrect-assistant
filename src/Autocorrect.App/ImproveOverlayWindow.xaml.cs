using System.Windows;

namespace Autocorrect.App;

public partial class ImproveOverlayWindow : Window
{
    public event EventHandler? ImproveClicked;

    public ImproveOverlayWindow()
    {
        InitializeComponent();
    }

    private void Improve_OnClick(object sender, RoutedEventArgs e)
    {
        ImproveClicked?.Invoke(this, EventArgs.Empty);
    }
}
