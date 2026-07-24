using System.Windows;
using System.Windows.Threading;

namespace Sentory.App;

public partial class SentoryDialogWindow : Window
{
    private DispatcherTimer? _countdownTimer;

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

    public static bool ConfirmWithCountdown(
        Window? owner,
        string heading,
        Func<int, string> messageFactory,
        string confirmText,
        bool isDarkTheme,
        int countdownSeconds)
    {
        ArgumentNullException.ThrowIfNull(messageFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            countdownSeconds,
            0);

        var dialog = new SentoryDialogWindow(
            heading,
            messageFactory(countdownSeconds),
            confirmText,
            isDarkTheme,
            danger: false);
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.StartCountdown(countdownSeconds, messageFactory);
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
            SentoryLocalization.Text("Confirm"),
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

    private void StartCountdown(
        int seconds,
        Func<int, string> messageFactory)
    {
        var remainingSeconds = seconds;
        _countdownTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Normal,
            (_, _) =>
            {
                remainingSeconds--;
                if (remainingSeconds <= 0)
                {
                    _countdownTimer?.Stop();
                    DialogResult = true;
                    return;
                }

                MessageText.Text = messageFactory(remainingSeconds);
            },
            Dispatcher);
        Closed += (_, _) =>
        {
            _countdownTimer?.Stop();
            _countdownTimer = null;
        };
        _countdownTimer.Start();
    }
}
