using System.Windows;
using Sentory.Core;

namespace Sentory.App;

public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow(
        string status,
        bool paused,
        bool startupEnabled,
        bool discordSupportEnabled,
        CaptureRuntimeState discordDetectionState,
        bool discordRepairNeeded,
        bool isDarkTheme)
    {
        InitializeComponent();
        SentoryTheme.Apply(Resources, isDarkTheme);
        SentoryTheme.ApplyDetectionStatus(
            Resources,
            discordDetectionState,
            isDarkTheme);
        StatusText.Text = status;
        var discordPresentation = DiscordDetectionUiPolicy.Resolve(
            discordSupportEnabled,
            discordDetectionState,
            discordRepairNeeded);
        DiscordDetectionStatusText.Text = discordPresentation.ShowRepairAction
            ? SentoryLocalization.Text("StateReconnect")
            : DiscordDetectionPresentation.GetLabel(discordDetectionState);
        DiscordDetectionPanel.Visibility = discordPresentation.ShowTrayStatus
            ? Visibility.Visible
            : Visibility.Collapsed;
        PauseText.Text = SentoryLocalization.Text(
            paused ? "ResumeDetection" : "PauseDetection");
        PauseIcon.Text = paused ? "\uE768" : "\uE769";
        PauseSwitchThumb.HorizontalAlignment = paused
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
        StartupCheck.Text = startupEnabled ? "\uE73E" : string.Empty;
        DiscordSupportCheck.Text = discordSupportEnabled
            ? "\uE73E"
            : string.Empty;
        DiscordRepairButton.Visibility =
            discordPresentation.ShowRepairAction
                ? Visibility.Visible
                : Visibility.Collapsed;
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
