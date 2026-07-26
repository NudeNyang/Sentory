using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncStorageMigrationResult(
    bool Migrated,
    int LegacyProjected,
    string DeviceId);

public sealed class SyncStorageMigrationService(
    SentoryDataPaths paths,
    ICaptureRepository captureRepository,
    SentorySettingsStore settingsStore)
{
    public async Task<SyncStorageMigrationResult> MigrateIfNeededAsync(
        SentorySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.SyncEnabled ||
            settings.SyncFolderPath is not { Length: > 0 } folderPath ||
            settings.SyncDeviceId is not { } currentDeviceId ||
            !SyncDeviceIdentity.IsValid(currentDeviceId))
        {
            throw new InvalidOperationException(
                "완전한 동기화 설정이 있어야 저장 형식을 이전할 수 있습니다.");
        }

        if (settings.SyncStorageVersion ==
                SentorySettings.CurrentSyncStorageVersion &&
            settings.SyncMigrationDeviceId is null)
        {
            return new SyncStorageMigrationResult(
                false,
                0,
                currentDeviceId);
        }

        var legacyProjected = 0;
        var migrationDeviceId = settings.SyncMigrationDeviceId;
        if (migrationDeviceId is null)
        {
            var legacy = await new LocalFolderSyncRuntimeService(
                paths,
                captureRepository).RunLegacyOnceAsync(
                    currentDeviceId,
                    folderPath,
                    cancellationToken);
            legacyProjected = legacy.Cycle.Projection.Projected;
            migrationDeviceId = SyncDeviceIdentity.Create();
            settings.SyncMigrationDeviceId = migrationDeviceId;
            settingsStore.Save(settings);
        }

        await SqliteSyncOperationJournal.ResetForNewStoreAsync(
            paths,
            migrationDeviceId,
            cancellationToken);

        var latest = settingsStore.Load();
        if (!latest.SyncEnabled ||
            !string.Equals(
                latest.SyncFolderPath,
                folderPath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal) ||
            !string.Equals(
                latest.SyncMigrationDeviceId,
                migrationDeviceId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "이전 중 동기화 설정이 변경되어 안전하게 완료할 수 없습니다.");
        }

        latest.SyncDeviceId = migrationDeviceId;
        latest.SyncStorageVersion =
            SentorySettings.CurrentSyncStorageVersion;
        latest.SyncMigrationDeviceId = null;
        settingsStore.Save(latest);
        return new SyncStorageMigrationResult(
            true,
            legacyProjected,
            migrationDeviceId);
    }
}
