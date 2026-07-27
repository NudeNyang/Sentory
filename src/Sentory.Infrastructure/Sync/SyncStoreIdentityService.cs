using System.Text.Json;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncStorePreparationResult(
    bool StoreReset,
    string? StoreId,
    string DeviceId);

public sealed class SyncStoreIdentityService(
    SentoryDataPaths paths,
    SentorySettingsStore settingsStore)
{
    private const int CurrentManifestVersion = 1;
    private const int MaximumManifestBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<SyncStorePreparationResult> PrepareAsync(
        string deviceId,
        string selectedDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedDirectory);
        var root = Path.GetFullPath(selectedDirectory);
        var manifestPath = Path.Combine(
            root,
            ".sentory",
            "v2",
            "store.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            var settings = settingsStore.Load();
            var manifest = await TryReadManifestAsync(
                manifestPath,
                cancellationToken);
            if (manifest is null)
            {
                var hasPublishedLocalHistory =
                    await SqliteSyncOperationJournal
                        .HasPublishedLocalHistoryAsync(
                            paths,
                            cancellationToken);
                var hasRemoteContent = HasRemoteSyncContent(root);
                manifest = await CreateOrReadManifestAsync(
                    manifestPath,
                    cancellationToken);
                var storeReset = settings.SyncStoreId is not null ||
                                 hasPublishedLocalHistory &&
                                 !hasRemoteContent;
                if (storeReset)
                {
                    deviceId = Sentory.Core.Sync.SyncDeviceIdentity.Create();
                    await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                        paths,
                        deviceId,
                        cancellationToken);
                    settings.SyncDeviceId = deviceId;
                }

                settings.SyncStoreId = manifest.StoreId;
                settingsStore.Save(settings);
                return new SyncStorePreparationResult(
                    storeReset,
                    manifest.StoreId,
                    deviceId);
            }

            if (settings.SyncStoreId is null)
            {
                if (await SqliteSyncOperationJournal
                        .HasPublishedLocalHistoryAsync(
                            paths,
                            cancellationToken))
                {
                    deviceId = Sentory.Core.Sync.SyncDeviceIdentity.Create();
                    await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                        paths,
                        deviceId,
                        cancellationToken);
                    settings.SyncDeviceId = deviceId;
                    settings.SyncStoreId = manifest.StoreId;
                    settingsStore.Save(settings);
                    return new SyncStorePreparationResult(
                        true,
                        manifest.StoreId,
                        deviceId);
                }

                settings.SyncStoreId = manifest.StoreId;
                settingsStore.Save(settings);
                return new SyncStorePreparationResult(
                    false,
                    manifest.StoreId,
                    deviceId);
            }

            if (string.Equals(
                    settings.SyncStoreId,
                    manifest.StoreId,
                    StringComparison.Ordinal))
            {
                return new SyncStorePreparationResult(
                    false,
                    manifest.StoreId,
                    deviceId);
            }

            deviceId = Sentory.Core.Sync.SyncDeviceIdentity.Create();
            await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                paths,
                deviceId,
                cancellationToken);
            settings.SyncStoreId = manifest.StoreId;
            settings.SyncDeviceId = deviceId;
            settingsStore.Save(settings);
            return new SyncStorePreparationResult(
                true,
                manifest.StoreId,
                deviceId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new Sentory.Core.Sync.SyncStoreUnavailableException(
                $"클라우드 동기화 저장소 식별 파일을 사용할 수 없습니다: {root}",
                exception);
        }
    }

    private static async Task<SyncStoreManifest?> TryReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var file = new FileInfo(manifestPath);
        if (file.Length <= 0 || file.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "클라우드 동기화 저장소 식별 파일 크기가 올바르지 않습니다.");
        }

        SyncStoreManifest? manifest;
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            manifest = await JsonSerializer.DeserializeAsync<SyncStoreManifest>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "클라우드 동기화 저장소 식별 파일을 읽을 수 없습니다.",
                exception);
        }

        if (manifest is null ||
            manifest.FormatVersion != CurrentManifestVersion ||
            !IsValidStoreId(manifest.StoreId))
        {
            throw new InvalidDataException(
                "클라우드 동기화 저장소 식별 정보가 올바르지 않습니다.");
        }

        return manifest;
    }

    private static async Task<SyncStoreManifest> CreateOrReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var manifest = new SyncStoreManifest(
            CurrentManifestVersion,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow);
        var temporaryPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                JsonOptions);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, manifestPath, overwrite: false);
                return manifest;
            }
            catch (IOException) when (File.Exists(manifestPath))
            {
                return await TryReadManifestAsync(
                           manifestPath,
                           cancellationToken) ??
                       throw new InvalidDataException(
                           "클라우드 동기화 저장소 식별 파일이 사라졌습니다.");
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsValidStoreId(string value) =>
        value.Length == 32 &&
        string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal) &&
        Guid.TryParseExact(value, "N", out _);

    private static bool HasRemoteSyncContent(string root)
    {
        var folders = new[]
        {
            Path.Combine(root, ".sentory", "v2", "objects"),
            Path.Combine(root, "Sentory Sync", "v1", "objects"),
            Path.Combine(root, "Photos"),
            Path.Combine(root, "Links")
        };
        return folders.Any(folder =>
            Directory.Exists(folder) &&
            Directory.EnumerateFiles(
                folder,
                "*",
                SearchOption.AllDirectories).Any());
    }

    private sealed record SyncStoreManifest(
        int FormatVersion,
        string StoreId,
        DateTimeOffset CreatedAt);
}
