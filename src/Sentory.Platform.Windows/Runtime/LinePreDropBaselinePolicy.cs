using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal static class LinePreDropBaselinePolicy
{
    public static LineAccessibilitySnapshot? TryGetCompleted(
        Task<LineAccessibilitySnapshot?>? baselineTask) =>
        baselineTask is { IsCompletedSuccessfully: true }
            ? baselineTask.Result
            : null;
}
