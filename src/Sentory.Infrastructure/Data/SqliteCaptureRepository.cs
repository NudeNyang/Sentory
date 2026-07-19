using Microsoft.Data.Sqlite;
using Sentory.Core;
using System.IO;
using System.Security.Cryptography;

namespace Sentory.Infrastructure.Data;

public sealed class SqliteCaptureRepository(SentoryDataPaths paths)
    : ICaptureRepository
{
    private const int CurrentSchemaVersion = 2;
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var schemaVersion = await GetPragmaIntAsync(
            connection,
            "user_version",
            cancellationToken);
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                "현재 Sentory보다 새로운 데이터베이스 형식입니다.");
        }

        await ExecuteNonQueryAsync(
            connection,
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS items (
                id TEXT NOT NULL PRIMARY KEY,
                kind TEXT NOT NULL,
                normalized_key TEXT NOT NULL UNIQUE,
                original_url TEXT NOT NULL,
                domain TEXT NOT NULL,
                created_at TEXT NOT NULL,
                last_captured_at TEXT NOT NULL,
                last_source_app TEXT NOT NULL,
                last_capture_method TEXT NOT NULL,
                delivery_status TEXT NOT NULL,
                capture_count INTEGER NOT NULL,
                share_count INTEGER NOT NULL,
                content_path TEXT NULL,
                content_hash TEXT NULL,
                mime_type TEXT NULL,
                byte_size INTEGER NULL,
                image_width INTEGER NULL,
                image_height INTEGER NULL,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                copy_count INTEGER NOT NULL DEFAULT 0,
                last_copied_at TEXT NULL,
                page_title TEXT NULL,
                page_description TEXT NULL,
                site_icon_path TEXT NULL,
                preview_image_path TEXT NULL,
                preview_status TEXT NULL,
                preview_fetched_at TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS capture_events (
                event_id TEXT NOT NULL PRIMARY KEY,
                item_id TEXT NOT NULL,
                source_app TEXT NOT NULL,
                capture_method TEXT NOT NULL,
                delivery_status TEXT NOT NULL,
                context_hash TEXT NOT NULL,
                captured_at TEXT NOT NULL,
                signals_json TEXT NOT NULL,
                FOREIGN KEY(item_id) REFERENCES items(id)
            );

            CREATE INDEX IF NOT EXISTS ix_items_last_captured_at
                ON items(last_captured_at DESC);
            CREATE INDEX IF NOT EXISTS ix_capture_events_item_id
                ON capture_events(item_id);
            """,
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "content_path",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "content_hash",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "mime_type",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "byte_size",
            "INTEGER NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "image_width",
            "INTEGER NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "image_height",
            "INTEGER NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "is_favorite",
            "INTEGER NOT NULL DEFAULT 0",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "copy_count",
            "INTEGER NOT NULL DEFAULT 0",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "last_copied_at",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "page_title",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "page_description",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "site_icon_path",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "preview_image_path",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "preview_status",
            "TEXT NULL",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "items",
            "preview_fetched_at",
            "TEXT NULL",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_items_is_favorite
                ON items(is_favorite, last_captured_at DESC);
            CREATE INDEX IF NOT EXISTS ix_items_last_copied_at
                ON items(last_copied_at DESC);
            CREATE INDEX IF NOT EXISTS ix_items_preview_fetched_at
                ON items(kind, preview_fetched_at);
            """,
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            $"PRAGMA user_version = {CurrentSchemaVersion};",
            cancellationToken);
        await VerifyDatabaseIntegrityAsync(connection, cancellationToken);
    }

    public async Task<CaptureResult> UpsertUrlAsync(
        UrlCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        var existingEventItemId = await GetEventItemIdAsync(
            connection,
            transaction,
            request.EventId,
            cancellationToken);
        if (existingEventItemId is not null)
        {
            var counts = await GetCountsAsync(
                connection,
                transaction,
                existingEventItemId.Value,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CaptureResult(
                existingEventItemId.Value,
                false,
                false,
                counts.CaptureCount,
                counts.ShareCount);
        }

        var existingItem = await GetItemAsync(
            connection,
            transaction,
            request.NormalizedUrl.Value,
            cancellationToken);
        var itemId = existingItem?.ItemId ?? Guid.NewGuid();
        var itemCreated = existingItem is null;
        var captureCount = (existingItem?.CaptureCount ?? 0) + 1;
        var shareCount = (existingItem?.ShareCount ?? 0) +
                         (request.DeliveryStatus == DeliveryStatus.Confirmed
                             ? 1
                             : 0);

        await UpsertItemAsync(
            connection,
            transaction,
            request,
            itemId,
            captureCount,
            shareCount,
            cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            request,
            itemId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CaptureResult(
            itemId,
            itemCreated,
            true,
            captureCount,
            shareCount);
    }

    public async Task<CaptureResult> UpsertImageAsync(
        ImageCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var calculatedHash = Convert.ToHexString(
            SHA256.HashData(request.PngBytes.Span));
        if (!string.Equals(
                calculatedHash,
                request.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Image hash does not match its PNG bytes.",
                nameof(request));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        var existingEventItemId = await GetEventItemIdAsync(
            connection,
            transaction,
            request.EventId,
            cancellationToken);
        if (existingEventItemId is not null)
        {
            var counts = await GetCountsAsync(
                connection,
                transaction,
                existingEventItemId.Value,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CaptureResult(
                existingEventItemId.Value,
                false,
                false,
                counts.CaptureCount,
                counts.ShareCount);
        }

        var normalizedHash = calculatedHash.ToLowerInvariant();
        var normalizedKey = $"sha256:{normalizedHash}";
        var existingItem = await GetItemAsync(
            connection,
            transaction,
            normalizedKey,
            cancellationToken);
        var itemId = existingItem?.ItemId ?? Guid.NewGuid();
        var itemCreated = existingItem is null;
        var captureCount = (existingItem?.CaptureCount ?? 0) + 1;
        var shareCount = (existingItem?.ShareCount ?? 0) +
                         (request.DeliveryStatus == DeliveryStatus.Confirmed
                             ? 1
                             : 0);
        var relativePath = Path.Combine("images", $"{normalizedHash}.png");
        var absolutePath = Path.Combine(paths.RootDirectory, relativePath);

        if (!File.Exists(absolutePath))
        {
            var temporaryPath =
                $"{absolutePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(
                temporaryPath,
                request.PngBytes.ToArray(),
                cancellationToken);
            try
            {
                File.Move(temporaryPath, absolutePath, overwrite: false);
            }
            catch (IOException) when (File.Exists(absolutePath))
            {
                File.Delete(temporaryPath);
            }
        }

        await UpsertImageItemAsync(
            connection,
            transaction,
            request,
            itemId,
            normalizedKey,
            relativePath,
            captureCount,
            shareCount,
            cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            request.EventId,
            itemId,
            request.SourceApp,
            request.CaptureMethod,
            request.DeliveryStatus,
            request.ContextHash,
            request.CapturedAt,
            request.ConfirmationSignals,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CaptureResult(
            itemId,
            itemCreated,
            true,
            captureCount,
            shareCount);
    }

    public async Task<IReadOnlyList<CapturedItemSummary>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, kind, original_url, normalized_key, domain,
                   last_source_app, last_capture_method, delivery_status,
                   capture_count, share_count, created_at, last_captured_at,
                   content_path, content_hash, is_favorite, copy_count,
                   last_copied_at, page_title, page_description,
                   site_icon_path, preview_image_path, preview_status,
                   preview_fetched_at,
                   (SELECT GROUP_CONCAT(DISTINCT source_app)
                    FROM capture_events
                    WHERE item_id = items.id) AS source_apps
            FROM items
            ORDER BY last_captured_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<CapturedItemSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CapturedItemSummary(
                Guid.Parse(reader.GetString(0)),
                Enum.Parse<ContentKind>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Enum.Parse<SourceApp>(reader.GetString(5)),
                Enum.Parse<CaptureMethod>(reader.GetString(6)),
                Enum.Parse<DeliveryStatus>(reader.GetString(7)),
                reader.GetInt32(8),
                reader.GetInt32(9),
                DateTimeOffset.Parse(reader.GetString(10)),
                DateTimeOffset.Parse(reader.GetString(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetInt32(14) != 0,
                reader.GetInt32(15),
                reader.IsDBNull(16)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(16)),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21)
                    ? null
                    : Enum.Parse<LinkPreviewStatus>(reader.GetString(21)),
                reader.IsDBNull(22)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(22)),
                ParseSourceApps(
                    reader.IsDBNull(23) ? null : reader.GetString(23),
                    Enum.Parse<SourceApp>(reader.GetString(5)))));
        }

        return results;
    }

    private static IReadOnlyList<SourceApp> ParseSourceApps(
        string? value,
        SourceApp fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [fallback];
        }

        var sources = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
            .Select(source => Enum.TryParse<SourceApp>(source, out var parsed)
                ? parsed
                : (SourceApp?)null)
            .OfType<SourceApp>()
            .Distinct()
            .ToArray();
        return sources.Length > 0 ? sources : [fallback];
    }

    public async Task<bool> SetFavoriteAsync(
        Guid itemId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE items
            SET is_favorite = $isFavorite
            WHERE id = $itemId;
            """;
        command.Parameters.AddWithValue(
            "$isFavorite",
            isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> RecordCopyAsync(
        Guid itemId,
        DateTimeOffset copiedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE items
            SET copy_count = copy_count + 1,
                last_copied_at = $copiedAt
            WHERE id = $itemId;
            """;
        command.Parameters.AddWithValue("$copiedAt", copiedAt.ToString("O"));
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        var storedPaths = new List<string>();
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = (SqliteTransaction)transaction;
            lookup.CommandText =
                """
                SELECT content_path, site_icon_path, preview_image_path
                FROM items
                WHERE id = $itemId;
                """;
            lookup.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
            await using var reader = await lookup.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            for (var index = 0; index < 3; index++)
            {
                if (!reader.IsDBNull(index))
                {
                    storedPaths.Add(reader.GetString(index));
                }
            }
        }

        await using (var deleteEvents = connection.CreateCommand())
        {
            deleteEvents.Transaction = (SqliteTransaction)transaction;
            deleteEvents.CommandText =
                "DELETE FROM capture_events WHERE item_id = $itemId;";
            deleteEvents.Parameters.AddWithValue(
                "$itemId",
                itemId.ToString("D"));
            await deleteEvents.ExecuteNonQueryAsync(cancellationToken);
        }

        int affected;
        await using (var deleteItem = connection.CreateCommand())
        {
            deleteItem.Transaction = (SqliteTransaction)transaction;
            deleteItem.CommandText =
                "DELETE FROM items WHERE id = $itemId;";
            deleteItem.Parameters.AddWithValue(
                "$itemId",
                itemId.ToString("D"));
            affected = await deleteItem.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (affected > 0)
        {
            foreach (var storedPath in storedPaths.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                DeleteStoredFile(storedPath);
            }
        }

        return affected > 0;
    }

    public async Task<BulkDeleteResult> DeleteItemsAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var requestedIds = itemIds.Distinct().ToArray();
        var deletedItems = 0;
        foreach (var itemId in requestedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await DeleteItemAsync(itemId, cancellationToken))
            {
                deletedItems++;
            }
        }

        return new BulkDeleteResult(
            requestedIds.Length,
            deletedItems,
            requestedIds.Length - deletedItems);
    }

    public async Task<StorageRepairResult> RepairStorageAsync(
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var comparison = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var referencedFiles = new HashSet<string>(comparison);
        var missingImageFiles = 0;

        await using (var connection = await OpenConnectionAsync(
                         cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT content_path
                FROM items
                WHERE kind = $kind AND content_path IS NOT NULL;
                """;
            command.Parameters.AddWithValue("$kind", ContentKind.Image.ToString());
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var absolutePath = TryResolveContentPath(reader.GetString(0));
                if (absolutePath is null || !File.Exists(absolutePath))
                {
                    missingImageFiles++;
                    continue;
                }

                referencedFiles.Add(absolutePath);
            }
        }

        await using (var connection = await OpenConnectionAsync(
                         cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT site_icon_path, preview_image_path
                FROM items
                WHERE site_icon_path IS NOT NULL OR
                      preview_image_path IS NOT NULL;
                """;
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                for (var index = 0; index < 2; index++)
                {
                    if (reader.IsDBNull(index))
                    {
                        continue;
                    }

                    var absolutePath = TryResolveContentPath(
                        reader.GetString(index));
                    if (absolutePath is not null && File.Exists(absolutePath))
                    {
                        referencedFiles.Add(absolutePath);
                    }
                }
            }
        }

        var orphanFilesDeleted = 0;
        var temporaryFilesDeleted = 0;
        var fileDeleteFailures = 0;
        foreach (var file in Directory.EnumerateFiles(
                     paths.ImagesDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isTemporary = file.EndsWith(
                ".tmp",
                StringComparison.OrdinalIgnoreCase);
            var isOrphanPng = string.Equals(
                                  Path.GetExtension(file),
                                  ".png",
                                  StringComparison.OrdinalIgnoreCase) &&
                              !referencedFiles.Contains(Path.GetFullPath(file));
            if (!isTemporary && !isOrphanPng)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                if (isTemporary)
                {
                    temporaryFilesDeleted++;
                }
                else
                {
                    orphanFilesDeleted++;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                fileDeleteFailures++;
            }
        }

        foreach (var file in Directory.EnumerateFiles(
                     paths.LinkPreviewsDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isTemporary = file.EndsWith(
                ".tmp",
                StringComparison.OrdinalIgnoreCase);
            if (!isTemporary && referencedFiles.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                if (isTemporary)
                {
                    temporaryFilesDeleted++;
                }
                else
                {
                    orphanFilesDeleted++;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                fileDeleteFailures++;
            }
        }

        return new StorageRepairResult(
            orphanFilesDeleted,
            temporaryFilesDeleted,
            missingImageFiles,
            fileDeleteFailures);
    }

    public async Task<DataStatistics> GetDataStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN is_favorite = 1 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN kind = $urlKind THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN kind = $imageKind THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN kind = $imageKind
                                     THEN COALESCE(byte_size, 0) ELSE 0 END), 0)
            FROM items;
            """;
        command.Parameters.AddWithValue("$urlKind", ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$imageKind",
            ContentKind.Image.ToString());
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DataStatistics(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt64(4));
    }

    public async Task<DataCleanupPreview> PreviewCleanupAsync(
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ReadCleanupPreviewAsync(
            connection,
            transaction: null,
            olderThan,
            cancellationToken);
    }

    public async Task<DataCleanupResult> CleanupAsync(
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);
        var preview = await ReadCleanupPreviewAsync(
            connection,
            transaction,
            olderThan,
            cancellationToken);
        var imagePaths = new List<string>();
        var previewPaths = new List<string>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"""
                SELECT content_path, site_icon_path, preview_image_path
                FROM items
                WHERE {CleanupPredicate}
                """;
            AddCleanupParameters(select, olderThan);
            await using var reader = await select.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    imagePaths.Add(reader.GetString(0));
                }

                for (var index = 1; index < 3; index++)
                {
                    if (!reader.IsDBNull(index))
                    {
                        previewPaths.Add(reader.GetString(index));
                    }
                }
            }
        }

        await using (var deleteEvents = connection.CreateCommand())
        {
            deleteEvents.Transaction = transaction;
            deleteEvents.CommandText =
                $"""
                DELETE FROM capture_events
                WHERE item_id IN (
                    SELECT id FROM items WHERE {CleanupPredicate}
                );
                """;
            AddCleanupParameters(deleteEvents, olderThan);
            await deleteEvents.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteItems = connection.CreateCommand())
        {
            deleteItems.Transaction = transaction;
            deleteItems.CommandText =
                $"DELETE FROM items WHERE {CleanupPredicate};";
            AddCleanupParameters(deleteItems, olderThan);
            await deleteItems.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var deletedImageFiles = 0;
        var fileDeleteFailures = 0;
        foreach (var relativePath in imagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var target = TryResolveContentPath(relativePath);
                if (target is null)
                {
                    fileDeleteFailures++;
                    continue;
                }

                if (File.Exists(target))
                {
                    File.Delete(target);
                    deletedImageFiles++;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                fileDeleteFailures++;
            }
        }

        foreach (var relativePath in previewPaths.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                DeleteStoredFile(relativePath);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                fileDeleteFailures++;
            }
        }

        return new DataCleanupResult(
            preview,
            deletedImageFiles,
            fileDeleteFailures);
    }

    private const string CleanupPredicate =
        "is_favorite = 0 AND " +
        "($olderThan IS NULL OR " +
        "julianday(last_captured_at) < julianday($olderThan))";

    private static async Task<DataCleanupPreview> ReadCleanupPreviewAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN kind = $urlKind THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN kind = $imageKind THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN kind = $imageKind
                                     THEN COALESCE(byte_size, 0) ELSE 0 END), 0)
            FROM items
            WHERE {CleanupPredicate};
            """;
        command.Parameters.AddWithValue("$urlKind", ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$imageKind",
            ContentKind.Image.ToString());
        AddCleanupParameters(command, olderThan);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DataCleanupPreview(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt64(3));
    }

    private static void AddCleanupParameters(
        SqliteCommand command,
        DateTimeOffset? olderThan)
    {
        command.Parameters.AddWithValue(
            "$olderThan",
            olderThan is null
                ? DBNull.Value
                : olderThan.Value.ToString("O"));
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

    public async Task<IReadOnlyList<LinkPreviewCandidate>>
        GetLinkPreviewCandidatesAsync(
            int limit,
            DateTimeOffset retryBefore,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, original_url, normalized_key
            FROM items
            WHERE kind = $kind
              AND (preview_fetched_at IS NULL OR
                   julianday(preview_fetched_at) < julianday($retryBefore))
            ORDER BY preview_fetched_at IS NOT NULL,
                     last_captured_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$kind", ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$retryBefore",
            retryBefore.ToString("O"));
        command.Parameters.AddWithValue("$limit", limit);
        var candidates = new List<LinkPreviewCandidate>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new LinkPreviewCandidate(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return candidates;
    }

    public async Task<bool> UpdateLinkPreviewAsync(
        Guid itemId,
        LinkPreviewUpdate preview,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);
        var oldPaths = new List<string>();
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText =
                """
                SELECT site_icon_path, preview_image_path
                FROM items
                WHERE id = $itemId AND kind = $kind;
                """;
            lookup.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
            lookup.Parameters.AddWithValue("$kind", ContentKind.Url.ToString());
            await using var reader = await lookup.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            for (var index = 0; index < 2; index++)
            {
                if (!reader.IsDBNull(index))
                {
                    oldPaths.Add(reader.GetString(index));
                }
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE items
            SET page_title = $pageTitle,
                page_description = $pageDescription,
                site_icon_path = $siteIconPath,
                preview_image_path = $previewImagePath,
                preview_status = $previewStatus,
                preview_fetched_at = $previewFetchedAt
            WHERE id = $itemId AND kind = $kind;
            """;
        command.Parameters.AddWithValue(
            "$pageTitle",
            (object?)preview.PageTitle ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$pageDescription",
            (object?)preview.PageDescription ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$siteIconPath",
            (object?)preview.SiteIconPath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$previewImagePath",
            (object?)preview.PreviewImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$previewStatus",
            preview.Status.ToString());
        command.Parameters.AddWithValue(
            "$previewFetchedAt",
            preview.FetchedAt.ToString("O"));
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue("$kind", ContentKind.Url.ToString());
        var updated = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        await transaction.CommitAsync(cancellationToken);
        if (updated)
        {
            var currentPaths = new HashSet<string>(
                new string?[]
                    { preview.SiteIconPath, preview.PreviewImagePath }
                    .OfType<string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var oldPath in oldPaths
                         .Where(path => !currentPaths.Contains(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    DeleteStoredFile(oldPath);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return updated;
    }

    private void DeleteStoredFile(string relativePath)
    {
        var target = TryResolveContentPath(relativePath);
        if (target is null)
        {
            throw new InvalidOperationException(
                "Stored content path escaped the Sentory data directory.");
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }
    }

    private string? TryResolveContentPath(string relativePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.GetFullPath(paths.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(
            Path.Combine(paths.RootDirectory, relativePath));
        return target.StartsWith(root, comparison) ? target : null;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetPragmaIntAsync(
        SqliteConnection connection,
        string pragmaName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName};";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task VerifyDatabaseIntegrityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Sentory 데이터베이스 무결성 검사에 실패했습니다.");
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await inspect.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(
                    reader.GetString(1),
                    column,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid?> GetEventItemIdAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            "SELECT item_id FROM capture_events WHERE event_id = $eventId;";
        command.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string value ? Guid.Parse(value) : null;
    }

    private static async Task<(Guid ItemId, int CaptureCount, int ShareCount)?>
        GetItemAsync(
            SqliteConnection connection,
            System.Data.Common.DbTransaction transaction,
            string normalizedKey,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            SELECT id, capture_count, share_count
            FROM items
            WHERE normalized_key = $normalizedKey;
            """;
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            Guid.Parse(reader.GetString(0)),
            reader.GetInt32(1),
            reader.GetInt32(2));
    }

    private static async Task<(int CaptureCount, int ShareCount)> GetCountsAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            SELECT capture_count, share_count
            FROM items
            WHERE id = $itemId;
            """;
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Capture event references a missing item.");
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task UpsertItemAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        UrlCaptureRequest request,
        Guid itemId,
        int captureCount,
        int shareCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO items (
                id, kind, normalized_key, original_url, domain,
                created_at, last_captured_at, last_source_app,
                last_capture_method, delivery_status,
                capture_count, share_count
            )
            VALUES (
                $id, $kind, $normalizedKey, $originalUrl, $domain,
                $createdAt, $lastCapturedAt, $lastSourceApp,
                $lastCaptureMethod, $deliveryStatus,
                $captureCount, $shareCount
            )
            ON CONFLICT(normalized_key) DO UPDATE SET
                original_url = excluded.original_url,
                domain = excluded.domain,
                last_captured_at = excluded.last_captured_at,
                last_source_app = excluded.last_source_app,
                last_capture_method = excluded.last_capture_method,
                delivery_status = excluded.delivery_status,
                capture_count = excluded.capture_count,
                share_count = excluded.share_count;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString("D"));
        command.Parameters.AddWithValue("$kind", ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$normalizedKey",
            request.NormalizedUrl.Value);
        command.Parameters.AddWithValue("$originalUrl", request.OriginalUrl);
        command.Parameters.AddWithValue("$domain", request.NormalizedUrl.Domain);
        command.Parameters.AddWithValue(
            "$createdAt",
            request.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$lastCapturedAt",
            request.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$lastSourceApp",
            request.SourceApp.ToString());
        command.Parameters.AddWithValue(
            "$lastCaptureMethod",
            request.CaptureMethod.ToString());
        command.Parameters.AddWithValue(
            "$deliveryStatus",
            request.DeliveryStatus.ToString());
        command.Parameters.AddWithValue("$captureCount", captureCount);
        command.Parameters.AddWithValue("$shareCount", shareCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        UrlCaptureRequest request,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await InsertEventAsync(
            connection,
            transaction,
            request.EventId,
            itemId,
            request.SourceApp,
            request.CaptureMethod,
            request.DeliveryStatus,
            request.ContextHash,
            request.CapturedAt,
            request.ConfirmationSignals,
            cancellationToken);
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid eventId,
        Guid itemId,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO capture_events (
                event_id, item_id, source_app, capture_method,
                delivery_status, context_hash, captured_at, signals_json
            )
            VALUES (
                $eventId, $itemId, $sourceApp, $captureMethod,
                $deliveryStatus, $contextHash, $capturedAt, $signalsJson
            );
            """;
        command.Parameters.AddWithValue(
            "$eventId",
            eventId.ToString("D"));
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue(
            "$sourceApp",
            sourceApp.ToString());
        command.Parameters.AddWithValue(
            "$captureMethod",
            captureMethod.ToString());
        command.Parameters.AddWithValue(
            "$deliveryStatus",
            deliveryStatus.ToString());
        command.Parameters.AddWithValue("$contextHash", contextHash);
        command.Parameters.AddWithValue(
            "$capturedAt",
            capturedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$signalsJson",
            System.Text.Json.JsonSerializer.Serialize(
                confirmationSignals));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertImageItemAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        ImageCaptureRequest request,
        Guid itemId,
        string normalizedKey,
        string relativePath,
        int captureCount,
        int shareCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO items (
                id, kind, normalized_key, original_url, domain,
                created_at, last_captured_at, last_source_app,
                last_capture_method, delivery_status,
                capture_count, share_count, content_path, content_hash,
                mime_type, byte_size, image_width, image_height
            )
            VALUES (
                $id, $kind, $normalizedKey, '', '',
                $createdAt, $lastCapturedAt, $lastSourceApp,
                $lastCaptureMethod, $deliveryStatus,
                $captureCount, $shareCount, $contentPath, $contentHash,
                'image/png', $byteSize, $imageWidth, $imageHeight
            )
            ON CONFLICT(normalized_key) DO UPDATE SET
                last_captured_at = excluded.last_captured_at,
                last_source_app = excluded.last_source_app,
                last_capture_method = excluded.last_capture_method,
                delivery_status = excluded.delivery_status,
                capture_count = excluded.capture_count,
                share_count = excluded.share_count,
                content_path = excluded.content_path,
                content_hash = excluded.content_hash,
                mime_type = excluded.mime_type,
                byte_size = excluded.byte_size,
                image_width = excluded.image_width,
                image_height = excluded.image_height;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString("D"));
        command.Parameters.AddWithValue("$kind", ContentKind.Image.ToString());
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        command.Parameters.AddWithValue(
            "$createdAt",
            request.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$lastCapturedAt",
            request.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$lastSourceApp",
            request.SourceApp.ToString());
        command.Parameters.AddWithValue(
            "$lastCaptureMethod",
            request.CaptureMethod.ToString());
        command.Parameters.AddWithValue(
            "$deliveryStatus",
            request.DeliveryStatus.ToString());
        command.Parameters.AddWithValue("$captureCount", captureCount);
        command.Parameters.AddWithValue("$shareCount", shareCount);
        command.Parameters.AddWithValue("$contentPath", relativePath);
        command.Parameters.AddWithValue(
            "$contentHash",
            request.Sha256.ToLowerInvariant());
        command.Parameters.AddWithValue(
            "$byteSize",
            request.PngBytes.Length);
        command.Parameters.AddWithValue("$imageWidth", request.PixelWidth);
        command.Parameters.AddWithValue("$imageHeight", request.PixelHeight);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
