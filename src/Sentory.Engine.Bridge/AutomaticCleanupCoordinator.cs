using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Engine.Bridge;

internal sealed class AutomaticCleanupCoordinator(
    ICaptureRepository repository,
    SentorySettingsStore settingsStore)
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(24);

    public async Task<DataCleanupResult?> RunIfDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var settings = settingsStore.Load();
        if (settings.AutoCleanupDays == 0 ||
            settings.LastAutoCleanupAt is { } lastCleanup &&
            now - lastCleanup < MinimumInterval)
        {
            return null;
        }

        var result = await repository.CleanupAsync(
            now.AddDays(-settings.AutoCleanupDays),
            cancellationToken);
        var latestSettings = settingsStore.Load();
        latestSettings.LastAutoCleanupAt = now;
        settingsStore.Save(latestSettings);
        return result;
    }
}
