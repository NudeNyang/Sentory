using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SyncStorageMigrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Sync.Migration.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrationPreservesV1AndReexportsGalleryAsReadableV2()
    {
        var dataRoot = Path.Combine(_root, "data");
        var cloudRoot = Path.Combine(_root, "cloud");
        var paths = SentoryDataPaths.ForRoot(dataRoot);
        var captures = new SqliteCaptureRepository(paths);
        await captures.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/migrate",
            out var normalized));
        await captures.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            normalized.Original,
            normalized,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "context",
            DateTimeOffset.Parse("2026-07-26T18:30:00+09:00"),
            ["url-match"]));
        byte[] imageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 7, 8, 9];
        await captures.UpsertImageAsync(new ImageCaptureRequest(
            Guid.NewGuid(),
            imageBytes,
            Convert.ToHexString(SHA256.HashData(imageBytes)),
            300,
            200,
            "image/png",
            ".png",
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "context",
            DateTimeOffset.Parse("2026-07-26T18:31:00+09:00"),
            ["clipboard-image"],
            "migration.png"));
        var oldDeviceId = SyncDeviceIdentity.Create();
        var journal = new SqliteSyncOperationJournal(paths, oldDeviceId);
        await journal.InitializeAsync();
        await new LocalFolderSyncRuntimeService(
            paths,
            captures).RunLegacyOnceAsync(oldDeviceId, cloudRoot);
        var v1Files = Directory.GetFiles(
            Path.Combine(cloudRoot, "Sentory Sync", "v1"),
            "*",
            SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(cloudRoot, path),
                path => Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(path))));
        var settingsStore = new SentorySettingsStore(paths);
        settingsStore.Save(new SentorySettings
        {
            SyncEnabled = true,
            SyncFolderPath = cloudRoot,
            SyncDeviceId = oldDeviceId,
            SyncStorageVersion = 1
        });

        var migration = await new SyncStorageMigrationService(
            paths,
            captures,
            settingsStore).MigrateIfNeededAsync(settingsStore.Load());
        await new LocalFolderSyncRuntimeService(
            paths,
            captures).RunOnceAsync(migration.DeviceId, cloudRoot);

        var migratedSettings = settingsStore.Load();
        Assert.True(migration.Migrated);
        Assert.NotEqual(oldDeviceId, migration.DeviceId);
        Assert.Equal(
            SentorySettings.CurrentSyncStorageVersion,
            migratedSettings.SyncStorageVersion);
        Assert.Null(migratedSettings.SyncMigrationDeviceId);
        Assert.Equal(migration.DeviceId, migratedSettings.SyncDeviceId);
        Assert.Single(Directory.GetFiles(
            Path.Combine(cloudRoot, "Photos"),
            "*.png"));
        Assert.Single(Directory.GetFiles(
            Path.Combine(cloudRoot, "Links"),
            "*.txt",
            SearchOption.AllDirectories));
        Assert.All(v1Files, pair =>
            Assert.Equal(
                pair.Value,
                Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(cloudRoot, pair.Key))))));
    }

    [Fact]
    public async Task PendingMigrationCanResumeAfterRestart()
    {
        var paths = SentoryDataPaths.ForRoot(Path.Combine(_root, "resume"));
        var captures = new SqliteCaptureRepository(paths);
        await captures.InitializeAsync();
        var oldDeviceId = SyncDeviceIdentity.Create();
        var migrationDeviceId = SyncDeviceIdentity.Create();
        var journal = new SqliteSyncOperationJournal(paths, oldDeviceId);
        await journal.InitializeAsync();
        var settingsStore = new SentorySettingsStore(paths);
        settingsStore.Save(new SentorySettings
        {
            SyncEnabled = true,
            SyncFolderPath = Path.Combine(_root, "resume-cloud"),
            SyncDeviceId = oldDeviceId,
            SyncStorageVersion = 1,
            SyncMigrationDeviceId = migrationDeviceId
        });

        var result = await new SyncStorageMigrationService(
            paths,
            captures,
            settingsStore).MigrateIfNeededAsync(settingsStore.Load());

        Assert.True(result.Migrated);
        Assert.Equal(migrationDeviceId, result.DeviceId);
        var settings = settingsStore.Load();
        Assert.Equal(migrationDeviceId, settings.SyncDeviceId);
        Assert.Null(settings.SyncMigrationDeviceId);
        var resetJournal = new SqliteSyncOperationJournal(
            paths,
            migrationDeviceId);
        await resetJournal.InitializeAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
