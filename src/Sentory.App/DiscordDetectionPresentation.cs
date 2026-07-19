using Sentory.Core;

namespace Sentory.App;

internal static class DiscordDetectionPresentation
{
    public static string GetLabel(CaptureRuntimeState state) =>
        state switch
        {
            CaptureRuntimeState.Ready => "감지 준비 완료",
            CaptureRuntimeState.ReconnectRequired => "Discord 재연결 필요",
            CaptureRuntimeState.Recovering => "워커 복구 중",
            _ => "연결 준비 중"
        };
}
