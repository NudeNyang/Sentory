using System.Windows;

namespace Sentory.App;

public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow(
        string status,
        bool paused,
        bool startupEnabled,
        bool discordSupportEnabled,
        bool isDarkTheme)
    {
        InitializeComponent();
        SentoryTheme.Apply(Resources, isDarkTheme);
        StatusText.Text = status;
        PauseText.Text = paused ? "감지 다시 시작" : "감지 일시정지";
        PauseIcon.Text = paused ? "\uE768" : "\uE769";
        PauseSwitchThumb.HorizontalAlignment = paused
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
        StartupCheck.Text = startupEnabled ? "\uE73E" : string.Empty;
        DiscordSupportCheck.Text = discordSupportEnabled
            ? "\uE73E"
            : string.Empty;
        DiscordRepairButton.IsEnabled = discordSupportEnabled;
        Deactivated += (_, _) => Close();
    }

    public event EventHandler? OpenGalleryRequested;
    public event EventHandler? PauseToggleRequested;
    public event EventHandler? StartupToggleRequested;
    public event EventHandler? DiscordSupportToggleRequested;
    public event EventHandler? DiscordRepairRequested;
    public event EventHandler? OpenDataRequested;
    public event EventHandler? ExitRequested;

    private void OpenGalleryButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(OpenGalleryRequested);

    private void PauseButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(PauseToggleRequested);

    private void StartupButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(StartupToggleRequested);

    private void DiscordSupportButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(DiscordSupportToggleRequested);

    private void DiscordRepairButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(DiscordRepairRequested);

    private void OpenDataButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(OpenDataRequested);

    private void ExitButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndClose(ExitRequested);

    private void RaiseAndClose(EventHandler? handler)
    {
        handler?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
