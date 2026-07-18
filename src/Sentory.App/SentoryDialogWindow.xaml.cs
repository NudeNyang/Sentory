using System.Windows;

namespace Sentory.App;

public partial class SentoryDialogWindow : Window
{
    private SentoryDialogWindow(
        string heading,
        string message,
        string confirmText,
        bool isDarkTheme,
        bool danger)
    {
        InitializeComponent();
        SentoryTheme.Apply(Resources, isDarkTheme);
        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Background = (System.Windows.Media.Brush)FindResource(
            danger ? "DangerBrush" : "AccentBrush");
        DialogIcon.Foreground = (System.Windows.Media.Brush)FindResource(
            danger ? "DangerBrush" : "AccentBrush");
        SourceInitialized += (_, _) =>
            SentoryTheme.ApplyTitleBar(this, isDarkTheme);
    }

    public static bool Confirm(
        Window? owner,
        string heading,
        string message,
        string confirmText,
        bool isDarkTheme,
        bool danger = false)
    {
        var dialog = new SentoryDialogWindow(
            heading,
            message,
            confirmText,
            isDarkTheme,
            danger);
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    public static void ShowMessage(
        Window? owner,
        string heading,
        string message,
        bool isDarkTheme,
        bool danger = false)
    {
        var dialog = new SentoryDialogWindow(
            heading,
            message,
            "확인",
            isDarkTheme,
            danger);
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _ = dialog.ShowDialog();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
