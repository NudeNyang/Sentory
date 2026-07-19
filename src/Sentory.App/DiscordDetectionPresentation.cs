using Sentory.Core;

namespace Sentory.App;

internal static class DiscordDetectionPresentation
{
    public static string GetLabel(CaptureRuntimeState state) =>
        state switch
        {
            CaptureRuntimeState.Ready =>
                SentoryLocalization.Text("StateReady"),
            CaptureRuntimeState.ReconnectRequired =>
                SentoryLocalization.Text("StateReconnect"),
            CaptureRuntimeState.Recovering =>
                SentoryLocalization.Text("StateRecovering"),
            _ => SentoryLocalization.Text("StateConnecting")
        };
}
