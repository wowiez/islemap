using System.Windows;
using System.Windows.Input;

namespace TheIsleOverlay.App;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(string currentVersion, string availableVersion)
    {
        InitializeComponent();
        VersionText.Text = $"v{currentVersion}  →  v{availableVersion}";
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
