namespace Sentory.App;

internal enum ManualUpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,
    Failed
}

internal readonly record struct ManualUpdateCheckResult(
    ManualUpdateCheckOutcome Outcome,
    string? Version = null);
