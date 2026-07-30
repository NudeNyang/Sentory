using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Engine.Bridge.Tests;

public sealed class AutomaticCleanupCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-automatic-cleanup-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunsConfiguredCleanupAndPersistsTheRunTime()
    {
        var now = DateTimeOffset.Parse("2026-07-30T03:00:00Z");
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/old",
            out var normalized));
        await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            "https://example.com/old",
            normalized,
            SourceApp.Line,
            CaptureMethod.LineConfirmedSend,
            DeliveryStatus.NotObserved,
            "cleanup-test",
            now.AddDays(-31),
            ["test"]));
        var settingsStore = new SentorySettingsStore(paths);
        var settings = settingsStore.Load();
        settings.AutoCleanupDays = 30;
        settings.LastAutoCleanupAt = null;
        settingsStore.Save(settings);
        var coordinator = new AutomaticCleanupCoordinator(
            repository,
            settingsStore);

        var result = await coordinator.RunIfDueAsync(now);

        Assert.NotNull(result);
        Assert.Equal(1, result.Deleted.TotalItems);
        Assert.Equal(now, settingsStore.Load().LastAutoCleanupAt);
    }

    [Fact]
    public async Task DoesNotRunMoreThanOnceWithinTwentyFourHours()
    {
        var now = DateTimeOffset.Parse("2026-07-30T03:00:00Z");
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var settingsStore = new SentorySettingsStore(paths);
        var settings = settingsStore.Load();
        settings.AutoCleanupDays = 30;
        settings.LastAutoCleanupAt = now.AddHours(-23);
        settingsStore.Save(settings);
        var coordinator = new AutomaticCleanupCoordinator(
            repository,
            settingsStore);

        var result = await coordinator.RunIfDueAsync(now);

        Assert.Null(result);
        Assert.Equal(now.AddHours(-23), settingsStore.Load().LastAutoCleanupAt);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
