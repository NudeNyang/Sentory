using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncMetadataRunResult(
    int Exported,
    int Projected,
    int AlreadyApplied,
    int Skipped,
    bool SettingsChanged);

public sealed class SyncMetadataService(
    SentoryDataPaths paths,
    SqliteSyncOperationJournal journal,
    SqliteCaptureRepository captureRepository,
    SentorySettingsStore? settingsStore = null)
{
    private static readonly Guid AutoFavoriteSettingsItemId =
        Guid.Parse("8c2cb86b-434c-4d57-8d70-8cf28ce0a6a4");
    private static readonly TimeSpan UsageSessionGap = TimeSpan.FromHours(6);
    private static readonly TimeSpan RecentUsageWindow = TimeSpan.FromDays(30);
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

    public async Task<int> CaptureLocalChangesAsync(
        CancellationToken cancellationToken = default)
    {
        var exported = await CaptureLocalSettingsAsync(cancellationToken);
        var items = await ReadLocalItemsAsync(cancellationToken);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await CreateLocalPayloadAsync(
                item,
                cancellationToken);
            var content = SyncMetadataPayloadSerializer.Serialize(payload);
            var fingerprint = Convert.ToHexString(
                    SHA256.HashData(content))
                .ToLowerInvariant();
            if (await IsExportedAsync(
                    item.ItemId,
                    fingerprint,
                    cancellationToken))
            {
                continue;
            }

            var occurredAt = Latest(
                item.LastCopiedAt,
                item.FavoriteChangedAt,
                payload.UsageSessions.LastOrDefault()?.LastEventAt) ??
                DateTimeOffset.UtcNow;
            var operation = await journal.AppendLocalAsync(
                item.ItemId,
                SyncOperationKind.Metadata,
                occurredAt,
                content,
                cancellationToken);
            await MarkLocalMetadataExportedAsync(
                item,
                payload,
                operation,
                cancellationToken);
            exported++;
        }

        return exported;
    }

    public async Task<SyncMetadataRunResult> ProjectReceivedAsync(
        CancellationToken cancellationToken = default)
    {
        var projected = 0;
        var alreadyApplied = 0;
        var skipped = 0;
        var settingsChanged = false;
        var deletionTimes = await ReadDeletionTimesAsync(cancellationToken);
        var operations = await journal.GetReceivedAsync(cancellationToken);
        foreach (var operation in operations.Where(operation =>
                     operation.Kind is SyncOperationKind.Metadata or
                         SyncOperationKind.Configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await WasAppliedAsync(
                    operation.OperationId,
                    cancellationToken))
            {
                alreadyApplied++;
                continue;
            }

            if (operation.Kind == SyncOperationKind.Configuration)
            {
                var changed = await ApplySettingsAsync(
                    operation,
                    cancellationToken);
                await MarkAppliedAsync(
                    operation.OperationId,
                    cancellationToken);
                settingsChanged |= changed;
                projected++;
                continue;
            }

            var payload = SyncMetadataPayloadSerializer.DeserializeItem(
                operation.Payload);
            var itemId = await FindItemIdAsync(
                payload.NormalizedKey,
                cancellationToken);
            if (itemId is null)
            {
                if (deletionTimes.TryGetValue(
                        payload.NormalizedKey,
                        out var missingItemDeletedAt) &&
                    missingItemDeletedAt >= payload.ItemCapturedAt)
                {
                    await MarkAppliedAsync(
                        operation.OperationId,
                        cancellationToken);
                    skipped++;
                }

                continue;
            }

            if (deletionTimes.TryGetValue(
                    payload.NormalizedKey,
                    out var deletedAt) &&
                deletedAt >= payload.ItemCapturedAt)
            {
                await MarkAppliedAsync(
                    operation.OperationId,
                    cancellationToken);
                skipped++;
                continue;
            }

            await ApplyItemMetadataAsync(
                itemId.Value,
                operation,
                payload,
                cancellationToken);
            await MarkAppliedAsync(
                operation.OperationId,
                cancellationToken);
            projected++;
        }

        return new SyncMetadataRunResult(
            Exported: 0,
            projected,
            alreadyApplied,
            skipped,
            settingsChanged);
    }

    private async Task<int> CaptureLocalSettingsAsync(
        CancellationToken cancellationToken)
    {
        if (settingsStore is null)
        {
            return 0;
        }

        var settings = settingsStore.Load();
        if (settings.AutoFavoriteChangedAt is null)
        {
            if (settings.AutoFavoriteEnabled ==
                    SentorySettings.DefaultAutoFavoriteEnabled &&
                settings.AutoFavoriteCopyThreshold ==
                SentorySettings.DefaultAutoFavoriteCopyThreshold)
            {
                return 0;
            }

            settings.AutoFavoriteChangedAt = DateTimeOffset.UtcNow;
            settingsStore.Save(settings);
        }

        var changedAt = settings.AutoFavoriteChangedAt.Value;
        if (await SettingsClockMatchesAsync(
                settings.AutoFavoriteEnabled,
                settings.AutoFavoriteCopyThreshold,
                changedAt,
                cancellationToken))
        {
            return 0;
        }

        var payload = new SyncAutoFavoriteSettingsPayload(
            SyncAutoFavoriteSettingsPayload.CurrentFormatVersion,
            settings.AutoFavoriteEnabled,
            settings.AutoFavoriteCopyThreshold,
            changedAt);
        var operation = await journal.AppendLocalAsync(
            AutoFavoriteSettingsItemId,
            SyncOperationKind.Configuration,
            changedAt,
            SyncMetadataPayloadSerializer.Serialize(payload),
            cancellationToken);
        await WriteSettingsClockAsync(
            payload,
            operation.DeviceId,
            operation.Sequence,
            cancellationToken);
        return 1;
    }

    private async Task<IReadOnlyList<LocalItem>> ReadLocalItemsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, normalized_key, last_captured_at,
                   copy_count, last_copied_at,
                   is_favorite, favorite_changed_at
            FROM items
            WHERE kind IN ($urlKind, $imageKind)
            ORDER BY id;
            """;
        command.Parameters.AddWithValue(
            "$urlKind",
            ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$imageKind",
            ContentKind.Image.ToString());
        var values = new List<LocalItem>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new LocalItem(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetInt64(3),
                ReadDate(reader, 4),
                reader.GetInt64(5) != 0,
                ReadDate(reader, 6)));
        }

        return values;
    }

    private async Task<SyncItemMetadataPayload> CreateLocalPayloadAsync(
        LocalItem item,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        var components = await ReadCopyComponentsAsync(
            connection,
            item.NormalizedKey,
            cancellationToken);
        var remoteTotal = components
            .Where(component => !string.Equals(
                component.DeviceId,
                journal.DeviceId,
                StringComparison.Ordinal))
            .Sum(component => component.CopyCount);
        var existingLocal = components.FirstOrDefault(component =>
            string.Equals(
                component.DeviceId,
                journal.DeviceId,
                StringComparison.Ordinal));
        var localCount = Math.Max(
            existingLocal?.CopyCount ?? 0,
            Math.Max(0, item.CopyCount - remoteTotal));
        var lastCopiedAt = Latest(
            existingLocal?.LastCopiedAt,
            item.LastCopiedAt);

        var sessions = await ReadRecentUsageSessionsAsync(
            connection,
            item.ItemId,
            cancellationToken);
        var favoriteNeedsExport = item.FavoriteChangedAt.HasValue &&
                                  !await FavoriteClockMatchesAsync(
                                      connection,
                                      item,
                                      cancellationToken);
        return new SyncItemMetadataPayload(
            SyncItemMetadataPayload.CurrentFormatVersion,
            item.NormalizedKey,
            item.LastCapturedAt,
            localCount,
            lastCopiedAt,
            favoriteNeedsExport ? item.IsFavorite : null,
            favoriteNeedsExport ? item.FavoriteChangedAt : null,
            sessions);
    }

    private async Task MarkLocalMetadataExportedAsync(
        LocalItem item,
        SyncItemMetadataPayload payload,
        SyncOperation operation,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await UpsertCopyComponentAsync(
            connection,
            transaction,
            payload.NormalizedKey,
            operation.DeviceId,
            payload.ItemCapturedAt,
            payload.DeviceCopyCount,
            payload.LastCopiedAt,
            cancellationToken);
        if (payload.IsFavorite.HasValue)
        {
            await WriteFavoriteClockAsync(
                connection,
                transaction,
                payload.NormalizedKey,
                payload.IsFavorite.Value,
                payload.FavoriteChangedAt!.Value,
                operation.DeviceId,
                operation.Sequence,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var stablePayload = payload.IsFavorite.HasValue
            ? payload with
            {
                IsFavorite = null,
                FavoriteChangedAt = null
            }
            : payload;
        var fingerprint = Convert.ToHexString(SHA256.HashData(
                SyncMetadataPayloadSerializer.Serialize(stablePayload)))
            .ToLowerInvariant();
        await using var markConnection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = markConnection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sync_item_metadata_exports (
                item_id, fingerprint, operation_id)
            VALUES ($itemId, $fingerprint, $operationId)
            ON CONFLICT(item_id) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                operation_id = excluded.operation_id;
            """;
        command.Parameters.AddWithValue(
            "$itemId",
            item.ItemId.ToString("D"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue(
            "$operationId",
            operation.OperationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProjectedSnapshotAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        LocalItem? item = null;
        await using (var readItem = connection.CreateCommand())
        {
            readItem.Transaction = (SqliteTransaction)transaction;
            readItem.CommandText =
                """
                SELECT id, normalized_key, last_captured_at,
                       copy_count, last_copied_at,
                       is_favorite, favorite_changed_at
                FROM items
                WHERE id = $itemId;
                """;
            readItem.Parameters.AddWithValue(
                "$itemId",
                itemId.ToString("D"));
            await using var reader = await readItem.ExecuteReaderAsync(
                cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                item = new LocalItem(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2)),
                    reader.GetInt64(3),
                    ReadDate(reader, 4),
                    reader.GetInt64(5) != 0,
                    ReadDate(reader, 6));
            }
        }

        if (item is null)
        {
            return;
        }

        long localCopyCount = 0;
        DateTimeOffset? localLastCopiedAt = null;
        await using (var readCopy = connection.CreateCommand())
        {
            readCopy.Transaction = (SqliteTransaction)transaction;
            readCopy.CommandText =
                """
                SELECT copy_count, last_copied_at
                FROM sync_item_copy_components
                WHERE normalized_key = $normalizedKey
                  AND device_id = $deviceId;
                """;
            readCopy.Parameters.AddWithValue(
                "$normalizedKey",
                item.NormalizedKey);
            readCopy.Parameters.AddWithValue("$deviceId", journal.DeviceId);
            await using var reader = await readCopy.ExecuteReaderAsync(
                cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                localCopyCount = reader.GetInt64(0);
                localLastCopiedAt = ReadDate(reader, 1);
            }
        }

        var favoriteNeedsExport = item.FavoriteChangedAt.HasValue &&
                                  !await FavoriteClockMatchesAsync(
                                      connection,
                                      transaction,
                                      item,
                                      cancellationToken);
        var sessions = new List<SyncUsageSession>();
        await using (var readSessions = connection.CreateCommand())
        {
            readSessions.Transaction = (SqliteTransaction)transaction;
            readSessions.CommandText =
                """
                SELECT session_started_at, last_event_at
                FROM usage_sessions
                WHERE item_id = $itemId
                  AND julianday(last_event_at) >= julianday($cutoff)
                ORDER BY julianday(session_started_at)
                LIMIT 512;
                """;
            readSessions.Parameters.AddWithValue(
                "$itemId",
                itemId.ToString("D"));
            readSessions.Parameters.AddWithValue(
                "$cutoff",
                (DateTimeOffset.UtcNow - RecentUsageWindow).ToString("O"));
            await using var reader = await readSessions.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sessions.Add(new SyncUsageSession(
                    DateTimeOffset.Parse(reader.GetString(0)),
                    DateTimeOffset.Parse(reader.GetString(1))));
            }
        }

        var payload = new SyncItemMetadataPayload(
            SyncItemMetadataPayload.CurrentFormatVersion,
            item.NormalizedKey,
            item.LastCapturedAt,
            localCopyCount,
            localLastCopiedAt,
            favoriteNeedsExport ? item.IsFavorite : null,
            favoriteNeedsExport ? item.FavoriteChangedAt : null,
            sessions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(
                SyncMetadataPayloadSerializer.Serialize(payload)))
            .ToLowerInvariant();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO sync_item_metadata_exports (
                item_id, fingerprint, operation_id)
            VALUES ($itemId, $fingerprint, $operationId)
            ON CONFLICT(item_id) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                operation_id = excluded.operation_id;
            """;
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue(
            "$operationId",
            operationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ApplyItemMetadataAsync(
        Guid itemId,
        SyncOperation operation,
        SyncItemMetadataPayload payload,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await UpsertCopyComponentAsync(
            connection,
            transaction,
            payload.NormalizedKey,
            operation.DeviceId,
            payload.ItemCapturedAt,
            payload.DeviceCopyCount,
            payload.LastCopiedAt,
            cancellationToken);

        var explicitFavoriteRemoved = false;
        if (payload.IsFavorite.HasValue &&
            await IncomingFavoriteWinsAsync(
                connection,
                transaction,
                payload.NormalizedKey,
                payload.FavoriteChangedAt!.Value,
                operation.DeviceId,
                operation.Sequence,
                cancellationToken))
        {
            await WriteFavoriteClockAsync(
                connection,
                transaction,
                payload.NormalizedKey,
                payload.IsFavorite.Value,
                payload.FavoriteChangedAt.Value,
                operation.DeviceId,
                operation.Sequence,
                cancellationToken);
            await using var favorite = connection.CreateCommand();
            favorite.Transaction = (SqliteTransaction)transaction;
            favorite.CommandText =
                """
                UPDATE items
                SET is_favorite = $isFavorite,
                    favorite_changed_at = $changedAt
                WHERE id = $itemId;
                """;
            favorite.Parameters.AddWithValue(
                "$isFavorite",
                payload.IsFavorite.Value ? 1 : 0);
            favorite.Parameters.AddWithValue(
                "$changedAt",
                payload.FavoriteChangedAt.Value.ToString("O"));
            favorite.Parameters.AddWithValue(
                "$itemId",
                itemId.ToString("D"));
            await favorite.ExecuteNonQueryAsync(cancellationToken);
            explicitFavoriteRemoved = !payload.IsFavorite.Value;
        }

        await MergeUsageSessionsAsync(
            connection,
            transaction,
            itemId,
            payload.UsageSessions,
            cancellationToken);
        await RecomputeCopyTotalAsync(
            connection,
            transaction,
            itemId,
            payload.NormalizedKey,
            cancellationToken);
        if (!explicitFavoriteRemoved)
        {
            await ApplyAutomaticFavoriteAsync(
                connection,
                transaction,
                itemId,
                cancellationToken);
        }
        await MarkProjectedSnapshotAsync(
            connection,
            transaction,
            itemId,
            operation.OperationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> ApplySettingsAsync(
        SyncOperation operation,
        CancellationToken cancellationToken)
    {
        var payload = SyncMetadataPayloadSerializer.DeserializeSettings(
            operation.Payload);
        if (!await IncomingSettingsWinsAsync(
                payload.ChangedAt,
                operation.DeviceId,
                operation.Sequence,
                cancellationToken))
        {
            return false;
        }

        await WriteSettingsClockAsync(
            payload,
            operation.DeviceId,
            operation.Sequence,
            cancellationToken);
        if (settingsStore is null)
        {
            return false;
        }

        var settings = settingsStore.Load();
        var changed = settings.AutoFavoriteEnabled != payload.Enabled ||
                      settings.AutoFavoriteCopyThreshold !=
                      payload.UsageThreshold;
        settings.AutoFavoriteEnabled = payload.Enabled;
        settings.AutoFavoriteCopyThreshold = payload.UsageThreshold;
        settings.AutoFavoriteChangedAt = payload.ChangedAt;
        settingsStore.Save(settings);
        captureRepository.ConfigureAutomaticFavorites(
            payload.Enabled,
            payload.UsageThreshold);
        if (payload.Enabled)
        {
            await ApplyAutomaticFavoritesToAllAsync(cancellationToken);
        }

        return changed;
    }

    private async Task ApplyAutomaticFavoriteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        if (settingsStore is null)
        {
            return;
        }

        var settings = settingsStore.Load();
        if (!settings.AutoFavoriteEnabled)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE items
            SET is_favorite = 1,
                favorite_changed_at = $changedAt
            WHERE id = $itemId
              AND is_favorite = 0
              AND copy_count + (
                  SELECT COUNT(*)
                  FROM usage_sessions AS recent_usage
                  WHERE recent_usage.item_id = $itemId
                    AND julianday(recent_usage.last_event_at) >=
                        julianday($cutoff)
                    AND recent_usage.session_id <> (
                        SELECT first_usage.session_id
                        FROM usage_sessions AS first_usage
                        WHERE first_usage.item_id = $itemId
                        ORDER BY julianday(first_usage.session_started_at),
                                 first_usage.session_id
                        LIMIT 1)
              ) >= $threshold;
            """;
        command.Parameters.AddWithValue(
            "$itemId",
            itemId.ToString("D"));
        command.Parameters.AddWithValue(
            "$threshold",
            settings.AutoFavoriteCopyThreshold);
        command.Parameters.AddWithValue(
            "$cutoff",
            (DateTimeOffset.UtcNow - RecentUsageWindow).ToString("O"));
        command.Parameters.AddWithValue(
            "$changedAt",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ApplyAutomaticFavoritesToAllAsync(
        CancellationToken cancellationToken)
    {
        if (settingsStore is null)
        {
            return;
        }

        var settings = settingsStore.Load();
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE items
            SET is_favorite = 1,
                favorite_changed_at = $changedAt
            WHERE kind IN ($urlKind, $imageKind)
              AND is_favorite = 0
              AND copy_count + (
                  SELECT COUNT(*)
                  FROM usage_sessions AS recent_usage
                  WHERE recent_usage.item_id = items.id
                    AND julianday(recent_usage.last_event_at) >=
                        julianday($cutoff)
                    AND recent_usage.session_id <> (
                        SELECT first_usage.session_id
                        FROM usage_sessions AS first_usage
                        WHERE first_usage.item_id = items.id
                        ORDER BY julianday(first_usage.session_started_at),
                                 first_usage.session_id
                        LIMIT 1)
              ) >= $threshold;
            """;
        command.Parameters.AddWithValue(
            "$urlKind",
            ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$imageKind",
            ContentKind.Image.ToString());
        command.Parameters.AddWithValue(
            "$threshold",
            settings.AutoFavoriteCopyThreshold);
        command.Parameters.AddWithValue(
            "$cutoff",
            (DateTimeOffset.UtcNow - RecentUsageWindow).ToString("O"));
        command.Parameters.AddWithValue(
            "$changedAt",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MergeUsageSessionsAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        IReadOnlyList<SyncUsageSession> incoming,
        CancellationToken cancellationToken)
    {
        if (incoming.Count == 0)
        {
            return;
        }

        var sessions = new List<SyncUsageSession>(incoming);
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = (SqliteTransaction)transaction;
            read.CommandText =
                """
                SELECT session_started_at, last_event_at
                FROM usage_sessions
                WHERE item_id = $itemId;
                """;
            read.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
            await using var reader = await read.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sessions.Add(new SyncUsageSession(
                    DateTimeOffset.Parse(reader.GetString(0)),
                    DateTimeOffset.Parse(reader.GetString(1))));
            }
        }

        var merged = MergeSessions(sessions);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText =
                "DELETE FROM usage_sessions WHERE item_id = $itemId;";
            delete.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var session in merged)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText =
                """
                INSERT INTO usage_sessions (
                    item_id, session_started_at, last_event_at)
                VALUES ($itemId, $startedAt, $lastEventAt);
                """;
            insert.Parameters.AddWithValue(
                "$itemId",
                itemId.ToString("D"));
            insert.Parameters.AddWithValue(
                "$startedAt",
                session.StartedAt.ToString("O"));
            insert.Parameters.AddWithValue(
                "$lastEventAt",
                session.LastEventAt.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<SyncUsageSession> MergeSessions(
        IEnumerable<SyncUsageSession> sessions)
    {
        var ordered = sessions
            .OrderBy(session => session.StartedAt)
            .ThenBy(session => session.LastEventAt)
            .ToList();
        var merged = new List<SyncUsageSession>();
        foreach (var session in ordered)
        {
            if (merged.Count == 0 ||
                session.StartedAt > merged[^1].LastEventAt + UsageSessionGap)
            {
                merged.Add(session);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = previous with
            {
                LastEventAt = session.LastEventAt > previous.LastEventAt
                    ? session.LastEventAt
                    : previous.LastEventAt
            };
        }

        return merged;
    }

    private async Task RecomputeCopyTotalAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        string normalizedKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE items
            SET copy_count = (
                    SELECT COALESCE(SUM(copy_count), 0)
                    FROM sync_item_copy_components
                    WHERE normalized_key = $normalizedKey),
                last_copied_at = (
                    SELECT last_copied_at
                    FROM sync_item_copy_components
                    WHERE normalized_key = $normalizedKey
                      AND last_copied_at IS NOT NULL
                    ORDER BY julianday(last_copied_at) DESC
                    LIMIT 1)
            WHERE id = $itemId;
            """;
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertCopyComponentAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string normalizedKey,
        string deviceId,
        DateTimeOffset itemCapturedAt,
        long copyCount,
        DateTimeOffset? lastCopiedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO sync_item_copy_components (
                normalized_key, device_id, item_captured_at,
                copy_count, last_copied_at)
            VALUES (
                $normalizedKey, $deviceId, $itemCapturedAt,
                $copyCount, $lastCopiedAt)
            ON CONFLICT(normalized_key, device_id) DO UPDATE SET
                copy_count = MAX(copy_count, excluded.copy_count),
                last_copied_at = CASE
                    WHEN last_copied_at IS NULL THEN excluded.last_copied_at
                    WHEN excluded.last_copied_at IS NULL THEN last_copied_at
                    WHEN julianday(excluded.last_copied_at) >
                         julianday(last_copied_at)
                    THEN excluded.last_copied_at
                    ELSE last_copied_at
                END;
            """;
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue(
            "$itemCapturedAt",
            itemCapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$copyCount", copyCount);
        command.Parameters.AddWithValue(
            "$lastCopiedAt",
            lastCopiedAt.HasValue
                ? lastCopiedAt.Value.ToString("O")
                : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CopyComponent>>
        ReadCopyComponentsAsync(
        SqliteConnection connection,
        string normalizedKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT device_id, copy_count, last_copied_at
            FROM sync_item_copy_components
            WHERE normalized_key = $normalizedKey;
            """;
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        var values = new List<CopyComponent>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new CopyComponent(
                reader.GetString(0),
                reader.GetInt64(1),
                ReadDate(reader, 2)));
        }

        return values;
    }

    private async Task<IReadOnlyList<SyncUsageSession>>
        ReadRecentUsageSessionsAsync(
        SqliteConnection connection,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT session_started_at, last_event_at
            FROM usage_sessions
            WHERE item_id = $itemId
              AND julianday(last_event_at) >= julianday($cutoff)
            ORDER BY julianday(session_started_at)
            LIMIT 512;
            """;
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue(
            "$cutoff",
            (DateTimeOffset.UtcNow - RecentUsageWindow).ToString("O"));
        var values = new List<SyncUsageSession>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new SyncUsageSession(
                DateTimeOffset.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1))));
        }

        return values;
    }

    private async Task<bool> IsExportedAsync(
        Guid itemId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 1
            FROM sync_item_metadata_exports
            WHERE item_id = $itemId
              AND fingerprint = $fingerprint;
            """;
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<Guid?> FindItemIdAsync(
        string normalizedKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id FROM items WHERE normalized_key = $normalizedKey;";
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        var value = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken));
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task<bool> WasAppliedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sync_metadata_applied WHERE operation_id = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task MarkAppliedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO sync_metadata_applied (
                operation_id, applied_at)
            VALUES ($id, $appliedAt);
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$appliedAt",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, DateTimeOffset>>
        ReadDeletionTimesAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, DateTimeOffset>(
            StringComparer.Ordinal);
        foreach (var operation in await journal.GetDeletionOperationsAsync(
                     cancellationToken))
        {
            var payload = SyncItemPayloadSerializer.Deserialize(
                operation.Payload);
            var key = payload.ContentKind switch
            {
                SyncItemContentKinds.Url => payload.Url!.NormalizedUrl,
                SyncItemContentKinds.Image =>
                    $"sha256:{payload.Image!.ContentSha256.ToLowerInvariant()}",
                _ => throw new InvalidDataException(
                    "삭제 동기화 키를 읽을 수 없습니다.")
            };
            if (!values.TryGetValue(key, out var existing) ||
                operation.OccurredAt > existing)
            {
                values[key] = operation.OccurredAt;
            }
        }

        return values;
    }

    private async Task<bool> IncomingFavoriteWinsAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string normalizedKey,
        DateTimeOffset changedAt,
        string deviceId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            SELECT changed_at, device_id, sequence
            FROM sync_item_favorite_clock
            WHERE normalized_key = $normalizedKey;
            """;
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return true;
        }

        return IsIncomingNewer(
            changedAt,
            deviceId,
            sequence,
            DateTimeOffset.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt64(2));
    }

    private static async Task<bool> FavoriteClockMatchesAsync(
        SqliteConnection connection,
        LocalItem item,
        CancellationToken cancellationToken)
        => await FavoriteClockMatchesAsync(
            connection,
            transaction: null,
            item,
            cancellationToken);

    private static async Task<bool> FavoriteClockMatchesAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        LocalItem item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText =
            """
            SELECT is_favorite, changed_at
            FROM sync_item_favorite_clock
            WHERE normalized_key = $normalizedKey;
            """;
        command.Parameters.AddWithValue(
            "$normalizedKey",
            item.NormalizedKey);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken) &&
               (reader.GetInt64(0) != 0) == item.IsFavorite &&
               DateTimeOffset.Parse(reader.GetString(1)) ==
               item.FavoriteChangedAt;
    }

    private static async Task WriteFavoriteClockAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string normalizedKey,
        bool isFavorite,
        DateTimeOffset changedAt,
        string deviceId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO sync_item_favorite_clock (
                normalized_key, is_favorite, changed_at, device_id, sequence)
            VALUES ($key, $favorite, $changedAt, $deviceId, $sequence)
            ON CONFLICT(normalized_key) DO UPDATE SET
                is_favorite = excluded.is_favorite,
                changed_at = excluded.changed_at,
                device_id = excluded.device_id,
                sequence = excluded.sequence;
            """;
        command.Parameters.AddWithValue("$key", normalizedKey);
        command.Parameters.AddWithValue("$favorite", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$changedAt", changedAt.ToString("O"));
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$sequence", sequence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> SettingsClockMatchesAsync(
        bool enabled,
        int threshold,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT enabled, usage_threshold, changed_at
            FROM sync_auto_favorite_clock
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken) &&
               (reader.GetInt64(0) != 0) == enabled &&
               reader.GetInt32(1) == threshold &&
               DateTimeOffset.Parse(reader.GetString(2)) == changedAt;
    }

    private async Task<bool> IncomingSettingsWinsAsync(
        DateTimeOffset changedAt,
        string deviceId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT changed_at, device_id, sequence
            FROM sync_auto_favorite_clock
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return true;
        }

        return IsIncomingNewer(
            changedAt,
            deviceId,
            sequence,
            DateTimeOffset.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt64(2));
    }

    private async Task WriteSettingsClockAsync(
        SyncAutoFavoriteSettingsPayload payload,
        string deviceId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sync_auto_favorite_clock (
                singleton_id, enabled, usage_threshold,
                changed_at, device_id, sequence)
            VALUES (1, $enabled, $threshold, $changedAt, $deviceId, $sequence)
            ON CONFLICT(singleton_id) DO UPDATE SET
                enabled = excluded.enabled,
                usage_threshold = excluded.usage_threshold,
                changed_at = excluded.changed_at,
                device_id = excluded.device_id,
                sequence = excluded.sequence;
            """;
        command.Parameters.AddWithValue("$enabled", payload.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$threshold", payload.UsageThreshold);
        command.Parameters.AddWithValue(
            "$changedAt",
            payload.ChangedAt.ToString("O"));
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$sequence", sequence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsIncomingNewer(
        DateTimeOffset incomingAt,
        string incomingDevice,
        long incomingSequence,
        DateTimeOffset currentAt,
        string currentDevice,
        long currentSequence)
    {
        var dateComparison = incomingAt.CompareTo(currentAt);
        if (dateComparison != 0)
        {
            return dateComparison > 0;
        }

        var deviceComparison = string.CompareOrdinal(
            incomingDevice,
            currentDevice);
        return deviceComparison > 0 ||
               deviceComparison == 0 && incomingSequence > currentSequence;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static DateTimeOffset? ReadDate(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal));

    private static DateTimeOffset? Latest(
        params DateTimeOffset?[] values)
    {
        DateTimeOffset? latest = null;
        foreach (var value in values)
        {
            if (value.HasValue &&
                (!latest.HasValue || value.Value > latest.Value))
            {
                latest = value.Value;
            }
        }

        return latest;
    }

    private sealed record LocalItem(
        Guid ItemId,
        string NormalizedKey,
        DateTimeOffset LastCapturedAt,
        long CopyCount,
        DateTimeOffset? LastCopiedAt,
        bool IsFavorite,
        DateTimeOffset? FavoriteChangedAt);

    private sealed record CopyComponent(
        string DeviceId,
        long CopyCount,
        DateTimeOffset? LastCopiedAt);
}
