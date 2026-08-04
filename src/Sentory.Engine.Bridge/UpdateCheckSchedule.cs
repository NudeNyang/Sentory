namespace Sentory.Engine.Bridge;

using Sentory.Infrastructure.Updates;

internal static class UpdateCheckSchedule
{
    internal static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(6);

    public static bool ShouldCheck(
        DateTimeOffset? lastCheckedAt,
        DateTimeOffset now,
        bool manual) =>
        manual ||
        lastCheckedAt is null ||
        now - lastCheckedAt.Value >= AutomaticInterval;
}

internal static class UpdatePackageKindDetector
{
    public static UpdatePackageKind Resolve(string applicationDirectory) =>
        File.Exists(Path.Combine(applicationDirectory, "unins000.exe"))
            ? UpdatePackageKind.Installer
            : UpdatePackageKind.Portable;
}
