using System.Windows;
using Sentory.Core;

namespace Sentory.App;

public partial class TrayMenuWindow : Window
{
    private bool _allowClose;

    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        Closing += (_, eventArgs) =>
        {
            if (_allowClose)
            {
                return;
            }

            eventArgs.Cancel = true;
            Hide();
        };
    }

    public void UpdateState(
        string status,
        bool paused,
        bool startupEnabled,
        bool discordSupportEnabled,
        bool discordProcessRunning,
        CaptureRuntimeState discordDetectionState,
        bool discordRepairNeeded,
        bool isDarkTheme)
    {
        SentoryTheme.Apply(Resources, isDarkTheme);
        SentoryTheme.ApplyDetectionStatus(
            Resources,
            discordDetectionState,
            isDarkTheme);
        StatusText.Text = status;
        var discordPresentation = DiscordDetectionUiPolicy.Resolve(
            discordSupportEnabled,
            discordProcessRunning,
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
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    public event EventHandler? OpenGalleryRequested;
    public event EventHandler? PauseToggleRequested;
    public event EventHandler? StartupToggleRequested;
    public event EventHandler? DiscordSupportToggleRequested;
    public event EventHandler? DiscordRepairRequested;
    public event EventHandler? OpenDataRequested;
    public event EventHandler? ExitRequested;

    private void OpenGalleryButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(OpenGalleryRequested);

    private void PauseButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(PauseToggleRequested);

    private void StartupButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(StartupToggleRequested);

    private void DiscordSupportButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(DiscordSupportToggleRequested);

    private void DiscordRepairButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(DiscordRepairRequested);

    private void OpenDataButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(OpenDataRequested);

    private void ExitButton_Click(object sender, RoutedEventArgs e) =>
        RaiseAndHide(ExitRequested);

    private void RaiseAndHide(EventHandler? handler)
    {
        handler?.Invoke(this, EventArgs.Empty);
        Hide();
    }
}
