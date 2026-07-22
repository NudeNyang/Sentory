namespace Sentory.App;

internal static class UpdateCheckSchedule
{
    private static readonly TimeSpan AutomaticInterval =
        TimeSpan.FromHours(6);

    public static bool ShouldCheck(
        DateTimeOffset? lastCheckedAt,
        DateTimeOffset now,
        bool ignoreCooldown) =>
        ignoreCooldown ||
        lastCheckedAt is null ||
        now - lastCheckedAt.Value >= AutomaticInterval;
}
