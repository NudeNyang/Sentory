using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;
using System.IO;
using System.Security.Cryptography;

namespace Sentory.Infrastructure.Data;

public sealed class SqliteCaptureRepository(SentoryDataPaths paths)
    : ICaptureRepository, IGalleryPageRepository, IImageOcrRepository,
      ISyncItemDeletionRepository
{
    private const int CurrentSchemaVersion = 7;
    private static readonly TimeSpan UsageSessionGap = TimeSpan.FromHours(6);
    private static readonly TimeSpan RecentUsageWindow = TimeSpan.FromDays(30);
    private static readonly HashSet<string> StoredImageExtensions = new(
        [".png", ".jpg", ".bmp", ".gif", ".tif", ".webp"],
        StringComparer.OrdinalIgnoreCase);
    private AutoFavoriteConfiguration _autoFavoriteConfiguration =
        new(false, 3);
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

    public void ConfigureAutomaticFavorites(
        bool enabled,
        int usageThreshold)
    {
        if (usageThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usageThreshold));
        }

        Volatile.Write(
            ref _autoFavoriteConfiguration,
            new AutoFavoriteConfiguration(enabled, usageThreshold));
    }

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
                favorite_changed_at TEXT NULL,
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

            CREATE TABLE IF NOT EXISTS usage_sessions (
                session_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                item_id TEXT NOT NULL,
                session_started_at TEXT NOT NULL,
                last_event_at TEXT NOT NULL,
                FOREIGN KEY(item_id) REFERENCES items(id)
                    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS collection_members (
                collection_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                kind TEXT NOT NULL,
                normalized_key TEXT NOT NULL,
                original_url TEXT NOT NULL,
                domain TEXT NOT NULL,
                content_path TEXT NULL,
                content_hash TEXT NULL,
                mime_type TEXT NULL,
                byte_size INTEGER NULL,
                image_width INTEGER NULL,
                image_height INTEGER NULL,
                PRIMARY KEY(collection_id, position),
                FOREIGN KEY(collection_id) REFERENCES items(id)
                    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS image_ocr (
                content_hash TEXT NOT NULL PRIMARY KEY,
                display_name TEXT NULL,
                recognized_text TEXT NOT NULL,
                status TEXT NOT NULL,
                language TEXT NULL,
                engine_name TEXT NOT NULL,
                processed_at TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 1,
                error_code TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_items_last_captured_at
                ON items(last_captured_at DESC);
            CREATE INDEX IF NOT EXISTS ix_capture_events_item_id
                ON capture_events(item_id);
            CREATE INDEX IF NOT EXISTS ix_capture_events_item_captured_at
                ON capture_events(item_id, julianday(captured_at) DESC);
            CREATE INDEX IF NOT EXISTS ix_usage_sessions_item_last_event
                ON usage_sessions(item_id, julianday(last_event_at) DESC);
            CREATE INDEX IF NOT EXISTS ix_collection_members_collection_id
                ON collection_members(collection_id, position);
            CREATE INDEX IF NOT EXISTS ix_collection_members_normalized_key
                ON collection_members(normalized_key);
            CREATE INDEX IF NOT EXISTS ix_image_ocr_status
                ON image_ocr(status, processed_at);

            DELETE FROM image_ocr
            WHERE NOT EXISTS (
                      SELECT 1
                      FROM items
                      WHERE lower(items.content_hash) = image_ocr.content_hash)
              AND NOT EXISTS (
                      SELECT 1
                      FROM collection_members
                      WHERE lower(collection_members.content_hash) =
                            image_ocr.content_hash);
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
            "favorite_changed_at",
            "TEXT NULL",
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
        await EnsureColumnAsync(
            connection,
            "collection_members",
            "byte_size",
            "INTEGER NULL",
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
        if (schemaVersion < 4)
        {
            await ExecuteNonQueryAsync(
                connection,
                """
                UPDATE items
                SET preview_status = NULL,
                    preview_fetched_at = NULL
                WHERE preview_image_path IS NULL
                  AND (
                      lower(domain) IN (
                          'youtube.com',
                          'www.youtube.com',
                          'm.youtube.com',
                          'music.youtube.com',
                          'youtu.be',
                          'www.youtu.be',
                          'youtube-nocookie.com',
                          'www.youtube-nocookie.com')
                      OR EXISTS (
                          SELECT 1
                          FROM collection_members
                          WHERE collection_id = items.id
                            AND lower(domain) IN (
                                'youtube.com',
                                'www.youtube.com',
                                'm.youtube.com',
                                'music.youtube.com',
                                'youtu.be',
                                'www.youtu.be',
                                'youtube-nocookie.com',
                                'www.youtube-nocookie.com')));
                """,
                cancellationToken);
        }
        if (schemaVersion < 6)
        {
            await BackfillUsageSessionsAsync(
                connection,
                cancellationToken);
            await ApplyAutomaticFavoritesAsync(
                connection,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        if (schemaVersion < 7)
        {
            await ExecuteNonQueryAsync(
                connection,
                """
                UPDATE items
                SET favorite_changed_at = last_captured_at
                WHERE is_favorite = 1
                  AND favorite_changed_at IS NULL;
                """,
                cancellationToken);
        }
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
        var itemId = await ResolveItemIdAsync(
            connection,
            transaction,
            existingItem?.ItemId,
            request.PreferredItemId,
            request.NormalizedUrl.Value,
            cancellationToken);
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
        var recentUsageSessionCount = await RecordUsageSessionAsync(
            connection,
            transaction,
            itemId,
            request.CapturedAt,
            cancellationToken);
        await ApplyAutomaticFavoriteAsync(
            connection,
            transaction,
            itemId,
            ContentKind.Url,
            recentUsageSessionCount,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CaptureResult(
            itemId,
            itemCreated,
            true,
            captureCount,
            shareCount,
            recentUsageSessionCount);
    }

    public async Task<CaptureResult> UpsertImageAsync(
        ImageCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var calculatedHash = Convert.ToHexString(
            SHA256.HashData(request.ContentBytes.Span));
        if (!string.Equals(
                calculatedHash,
                request.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Image hash does not match its content bytes.",
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
        var itemId = await ResolveItemIdAsync(
            connection,
            transaction,
            existingItem?.ItemId,
            request.PreferredItemId,
            normalizedKey,
            cancellationToken);
        var itemCreated = existingItem is null;
        var captureCount = (existingItem?.CaptureCount ?? 0) + 1;
        var shareCount = (existingItem?.ShareCount ?? 0) +
                         (request.DeliveryStatus == DeliveryStatus.Confirmed
                             ? 1
                             : 0);
        var relativePath = await EnsureImageStoredAsync(
            normalizedHash,
            request.FileExtension,
            request.ContentBytes,
            cancellationToken);

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
        var recentUsageSessionCount = await RecordUsageSessionAsync(
            connection,
            transaction,
            itemId,
            request.CapturedAt,
            cancellationToken);
        await ApplyAutomaticFavoriteAsync(
            connection,
            transaction,
            itemId,
            ContentKind.Image,
            recentUsageSessionCount,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CaptureResult(
            itemId,
            itemCreated,
            true,
            captureCount,
            shareCount,
            recentUsageSessionCount);
    }

    public async Task<CaptureResult> UpsertCollectionAsync(
        CollectionCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Members.Count < 2)
        {
            throw new ArgumentException(
                "A collection must contain at least two members.",
                nameof(request));
        }

        var distinctKeys = request.Members
            .Select(member => $"{member.Kind}:{member.NormalizedKey}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctKeys != request.Members.Count)
        {
            throw new ArgumentException(
                "A collection cannot contain duplicate members.",
                nameof(request));
        }

        if (!string.Equals(
                request.Signature,
                CaptureCollectionIdentity.CreateSignature(request.Members),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Collection signature does not match its members.",
                nameof(request));
        }

        foreach (var member in request.Members.Where(member =>
                     member.Kind == ContentKind.Image))
        {
            var calculatedHash = Convert.ToHexString(
                SHA256.HashData(member.ContentBytes.Span));
            if (!string.Equals(
                    calculatedHash,
                    member.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Collection image hash does not match its content bytes.",
                    nameof(request));
            }
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
            var existingCounts = await GetCountsAsync(
                connection,
                transaction,
                existingEventItemId.Value,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CaptureResult(
                existingEventItemId.Value,
                false,
                false,
                existingCounts.CaptureCount,
                existingCounts.ShareCount);
        }

        var storedPaths = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var member in request.Members.Where(member =>
                     member.Kind == ContentKind.Image))
        {
            storedPaths[member.NormalizedKey] = await EnsureImageStoredAsync(
                member.Sha256!,
                member.FileExtension!,
                member.ContentBytes,
                cancellationToken);
        }

        var normalizedKey = $"collection:sha256:{request.Signature}";
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

        await UpsertCollectionItemAsync(
            connection,
            transaction,
            request,
            itemId,
            normalizedKey,
            captureCount,
            shareCount,
            cancellationToken);
        await ReplaceCollectionMembersAsync(
            connection,
            transaction,
            request.Members,
            storedPaths,
            itemId,
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
                   content_path, items.content_hash, is_favorite, copy_count,
                   last_copied_at, page_title, page_description,
                   site_icon_path, preview_image_path, preview_status,
                   preview_fetched_at, mime_type,
                   (SELECT GROUP_CONCAT(DISTINCT source_app)
                    FROM capture_events
                    WHERE item_id = items.id) AS source_apps,
                   image_ocr.display_name, image_ocr.recognized_text,
                   image_ocr.status, image_ocr.language,
                   image_width, image_height
            FROM items
            LEFT JOIN image_ocr
              ON image_ocr.content_hash = lower(items.content_hash)
            ORDER BY last_captured_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<CapturedItemSummary>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadCapturedItemSummary(reader));
            }
        }

        var collections = results
            .Where(item => item.Kind == ContentKind.Collection)
            .Select(item => item.ItemId)
            .ToArray();
        if (collections.Length == 0)
        {
            return results;
        }

        var memberLookup = await ReadCollectionMembersAsync(
            connection,
            collections,
            cancellationToken);
        return results.Select(item => item.Kind == ContentKind.Collection
                ? item with
                {
                    Members = memberLookup.GetValueOrDefault(item.ItemId, [])
                }
                : item)
            .ToArray();
    }

    public async Task<GalleryPageResult> GetGalleryPageAsync(
        GalleryPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (request.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (request.Limit <= 0)
        {
            return new GalleryPageResult(0, []);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var countCommand = connection.CreateCommand();
        var countWhere = ConfigureGalleryQuery(countCommand, request);
        countCommand.CommandText =
            $"""
            SELECT COUNT(*)
            FROM items
            LEFT JOIN image_ocr
              ON image_ocr.content_hash = lower(items.content_hash)
            {countWhere};
            """;
        var total = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync(cancellationToken));
        if (total == 0 || request.Offset >= total)
        {
            return new GalleryPageResult(total, []);
        }

        await using var pageCommand = connection.CreateCommand();
        var pageWhere = ConfigureGalleryQuery(pageCommand, request);
        pageCommand.CommandText =
            $"""
            SELECT id, kind, original_url, normalized_key, domain,
                   last_source_app, last_capture_method, delivery_status,
                   capture_count, share_count, created_at, last_captured_at,
                   content_path, items.content_hash, is_favorite, copy_count,
                   last_copied_at, page_title, page_description,
                   site_icon_path, preview_image_path, preview_status,
                   preview_fetched_at, mime_type,
                   (SELECT GROUP_CONCAT(DISTINCT source_app)
                    FROM capture_events
                    WHERE item_id = items.id) AS source_apps,
                   image_ocr.display_name, image_ocr.recognized_text,
                   image_ocr.status, image_ocr.language,
                   image_width, image_height
            FROM items
            LEFT JOIN image_ocr
              ON image_ocr.content_hash = lower(items.content_hash)
            {pageWhere}
            ORDER BY {GetGalleryOrderBy(request.Options.SortMode)}
            LIMIT $limit OFFSET $offset;
            """;
        pageCommand.Parameters.AddWithValue("$limit", request.Limit);
        pageCommand.Parameters.AddWithValue("$offset", request.Offset);

        var results = new List<CapturedItemSummary>(request.Limit);
        await using (var reader =
                     await pageCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadCapturedItemSummary(reader));
            }
        }

        var collections = results
            .Where(item => item.Kind == ContentKind.Collection)
            .Select(item => item.ItemId)
            .ToArray();
        if (collections.Length > 0)
        {
            var memberLookup = await ReadCollectionMembersAsync(
                connection,
                collections,
                cancellationToken);
            results = results.Select(item => item.Kind == ContentKind.Collection
                    ? item with
                    {
                        Members = memberLookup.GetValueOrDefault(item.ItemId, [])
                    }
                    : item)
                .ToList();
        }

        return new GalleryPageResult(total, results);
    }

    private static string ConfigureGalleryQuery(
        SqliteCommand command,
        GalleryPageRequest request)
    {
        var options = request.Options;
        var predicates = new List<string>();
        if (options.Kind is { } kind)
        {
            predicates.Add(
                """
                (items.kind = $kind OR
                 (items.kind = $collectionKind AND EXISTS (
                    SELECT 1 FROM collection_members AS kind_members
                    WHERE kind_members.collection_id = items.id
                      AND kind_members.kind = $kind)))
                """);
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue(
                "$collectionKind",
                ContentKind.Collection.ToString());
        }
        if (options.FavoritesOnly)
        {
            predicates.Add("items.is_favorite <> 0");
        }
        if (options.SourceApps is { Count: > 0 })
        {
            var sourceParameters = options.SourceApps
                .OrderBy(source => source)
                .Select((source, index) =>
                {
                    var name = $"$source{index}";
                    command.Parameters.AddWithValue(name, source.ToString());
                    return name;
                })
                .ToArray();
            predicates.Add(
                $"items.last_source_app IN ({string.Join(", ", sourceParameters)})");
        }

        var dateStart = GetGalleryDateStart(request.Now, options.DateRange);
        if (dateStart is { } start)
        {
            predicates.Add(
                "julianday(items.last_captured_at) >= julianday($dateStart)");
            command.Parameters.AddWithValue("$dateStart", start.ToString("O"));
        }

        var search = options.SearchText.Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            predicates.Add(
                """
                (instr(lower(items.original_url), $search) > 0 OR
                 instr(lower(items.normalized_key), $search) > 0 OR
                 instr(lower(items.domain), $search) > 0 OR
                 instr(lower(coalesce(items.page_title, '')), $search) > 0 OR
                 instr(lower(coalesce(items.page_description, '')), $search) > 0 OR
                 instr(lower(coalesce(image_ocr.display_name, '')), $search) > 0 OR
                 instr(lower(coalesce(image_ocr.recognized_text, '')), $search) > 0 OR
                 EXISTS (
                    SELECT 1
                    FROM collection_members AS search_members
                    LEFT JOIN image_ocr AS member_ocr
                      ON member_ocr.content_hash = lower(search_members.content_hash)
                    WHERE search_members.collection_id = items.id AND
                         (instr(lower(search_members.original_url), $search) > 0 OR
                          instr(lower(search_members.normalized_key), $search) > 0 OR
                          instr(lower(search_members.domain), $search) > 0 OR
                          instr(lower(coalesce(member_ocr.display_name, '')), $search) > 0 OR
                          instr(lower(coalesce(member_ocr.recognized_text, '')), $search) > 0)))
                """);
            command.Parameters.AddWithValue("$search", search);
        }

        return predicates.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", predicates)}";
    }

    private static DateTimeOffset? GetGalleryDateStart(
        DateTimeOffset now,
        GalleryDateRange range) => range switch
        {
            GalleryDateRange.All => null,
            GalleryDateRange.Today => new DateTimeOffset(
                now.Year,
                now.Month,
                now.Day,
                0,
                0,
                0,
                now.Offset),
            GalleryDateRange.Last7Days => now.AddDays(-7),
            GalleryDateRange.Last30Days => now.AddDays(-30),
            _ => throw new ArgumentOutOfRangeException(nameof(range))
        };

    private static string GetGalleryOrderBy(GallerySortMode sortMode) =>
        sortMode switch
        {
            GallerySortMode.Newest =>
                "items.last_captured_at DESC, items.created_at DESC",
            GallerySortMode.Oldest =>
                "items.last_captured_at ASC, items.created_at ASC",
            GallerySortMode.MostCaptured =>
                "items.capture_count DESC, items.last_captured_at DESC",
            GallerySortMode.MostCopied =>
                "items.copy_count DESC, items.last_copied_at DESC, " +
                "items.last_captured_at DESC",
            GallerySortMode.RecentlyCopied =>
                "(items.last_copied_at IS NOT NULL) DESC, " +
                "items.last_copied_at DESC, items.last_captured_at DESC",
            GallerySortMode.Name =>
                "CASE " +
                "WHEN items.kind = 'Url' THEN " +
                "coalesce(nullif(items.page_title, ''), " +
                "nullif(items.domain, ''), items.normalized_key) " +
                "WHEN items.kind = 'Image' THEN " +
                "coalesce(nullif(image_ocr.display_name, ''), '클립보드 이미지') " +
                "ELSE '묶음' END COLLATE NOCASE ASC, " +
                "items.last_captured_at DESC",
            _ => throw new ArgumentOutOfRangeException(nameof(sortMode))
        };

    private static CapturedItemSummary ReadCapturedItemSummary(
        SqliteDataReader reader)
    {
        var fallbackSource = Enum.Parse<SourceApp>(reader.GetString(5));
        return new CapturedItemSummary(
            Guid.Parse(reader.GetString(0)),
            Enum.Parse<ContentKind>(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            fallbackSource,
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
                reader.IsDBNull(24) ? null : reader.GetString(24),
                fallbackSource),
            reader.IsDBNull(23) ? null : reader.GetString(23),
            OcrDisplayName: reader.IsDBNull(25) ? null : reader.GetString(25),
            OcrText: reader.IsDBNull(26) ? null : reader.GetString(26),
            OcrStatus: reader.IsDBNull(27)
                ? null
                : Enum.Parse<ImageOcrStatus>(reader.GetString(27)),
            OcrLanguage: reader.IsDBNull(28) ? null : reader.GetString(28),
            PixelWidth: reader.IsDBNull(29) ? null : reader.GetInt32(29),
            PixelHeight: reader.IsDBNull(30) ? null : reader.GetInt32(30));
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

    public async Task<IReadOnlyList<ImageOcrCandidate>>
        GetPendingImageOcrAsync(
            string engineName,
            int limit,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineName);
        if (limit <= 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH stored_images AS (
                SELECT lower(content_hash) AS content_hash,
                       content_path,
                       last_captured_at
                FROM items
                WHERE kind = $imageKind
                  AND content_hash IS NOT NULL
                  AND content_path IS NOT NULL
                UNION ALL
                SELECT lower(members.content_hash) AS content_hash,
                       members.content_path,
                       items.last_captured_at
                FROM collection_members AS members
                INNER JOIN items ON items.id = members.collection_id
                WHERE members.kind = $imageKind
                  AND members.content_hash IS NOT NULL
                  AND members.content_path IS NOT NULL
            )
            SELECT stored_images.content_hash,
                   min(stored_images.content_path)
            FROM stored_images
            LEFT JOIN image_ocr
              ON image_ocr.content_hash = stored_images.content_hash
            WHERE image_ocr.content_hash IS NULL OR
                  image_ocr.engine_name <> $engineName OR
                  (image_ocr.status = $failedStatus AND
                   image_ocr.attempt_count < 2 AND
                   julianday(image_ocr.processed_at) < julianday($retryBefore))
            GROUP BY stored_images.content_hash
            ORDER BY max(stored_images.last_captured_at) DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$imageKind",
            ContentKind.Image.ToString());
        command.Parameters.AddWithValue(
            "$failedStatus",
            ImageOcrStatus.Failed.ToString());
        command.Parameters.AddWithValue("$engineName", engineName);
        command.Parameters.AddWithValue(
            "$retryBefore",
            DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O"));
        command.Parameters.AddWithValue("$limit", limit);

        var candidates = new List<ImageOcrCandidate>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new ImageOcrCandidate(
                reader.GetString(0),
                reader.GetString(1)));
        }

        return candidates;
    }

    public async Task<bool> UpsertImageOcrAsync(
        ImageOcrUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Sha256);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO image_ocr (
                content_hash, display_name, recognized_text, status,
                language, engine_name, processed_at, attempt_count,
                error_code
            ) VALUES (
                $contentHash, $displayName, $recognizedText, $status,
                $language, $engineName, $processedAt, 1, $errorCode
            )
            ON CONFLICT(content_hash) DO UPDATE SET
                display_name = excluded.display_name,
                recognized_text = excluded.recognized_text,
                status = excluded.status,
                language = excluded.language,
                engine_name = excluded.engine_name,
                processed_at = excluded.processed_at,
                attempt_count = CASE
                    WHEN image_ocr.engine_name = excluded.engine_name
                    THEN image_ocr.attempt_count + 1
                    ELSE 1
                END,
                error_code = excluded.error_code;
            """;
        command.Parameters.AddWithValue(
            "$contentHash",
            update.Sha256.ToLowerInvariant());
        command.Parameters.AddWithValue(
            "$displayName",
            (object?)update.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$recognizedText",
            update.RecognizedText);
        command.Parameters.AddWithValue("$status", update.Status.ToString());
        command.Parameters.AddWithValue(
            "$language",
            (object?)update.Language ?? DBNull.Value);
        command.Parameters.AddWithValue("$engineName", update.EngineName);
        command.Parameters.AddWithValue(
            "$processedAt",
            update.ProcessedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$errorCode",
            (object?)update.ErrorCode ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
            SET is_favorite = $isFavorite,
                favorite_changed_at = $changedAt
            WHERE id = $itemId;
            """;
        command.Parameters.AddWithValue(
            "$isFavorite",
            isFavorite ? 1 : 0);
        command.Parameters.AddWithValue(
            "$changedAt",
            DateTimeOffset.UtcNow.ToString("O"));
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
        CancellationToken cancellationToken = default) =>
        await DeleteItemCoreAsync(
            itemId,
            normalizedKey: null,
            deletedAt: DateTimeOffset.UtcNow,
            appendSyncDeletion: true,
            cancellationToken);

    public async Task<bool> ApplySyncedDeletionAsync(
        string normalizedKey,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedKey);
        return await DeleteItemCoreAsync(
            itemId: null,
            normalizedKey,
            deletedAt,
            appendSyncDeletion: false,
            cancellationToken);
    }

    private async Task<bool> DeleteItemCoreAsync(
        Guid? itemId,
        string? normalizedKey,
        DateTimeOffset deletedAt,
        bool appendSyncDeletion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        Guid resolvedItemId;
        string resolvedNormalizedKey;
        ContentKind kind;
        DateTimeOffset lastCapturedAt;
        var storedPaths = new List<string>();
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = (SqliteTransaction)transaction;
            lookup.CommandText =
                """
                SELECT id, kind, normalized_key, last_captured_at,
                       content_path, site_icon_path, preview_image_path
                FROM items
                WHERE ($normalizedKey IS NOT NULL AND
                       normalized_key = $normalizedKey) OR
                      ($normalizedKey IS NULL AND id = $itemId)
                LIMIT 1;
                """;
            lookup.Parameters.AddWithValue(
                "$itemId",
                itemId is null
                    ? DBNull.Value
                    : itemId.Value.ToString("D"));
            lookup.Parameters.AddWithValue(
                "$normalizedKey",
                (object?)normalizedKey ?? DBNull.Value);
            await using var reader = await lookup.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            resolvedItemId = Guid.Parse(reader.GetString(0));
            resolvedNormalizedKey = reader.GetString(2);
            if (!Enum.TryParse(reader.GetString(1), out kind))
            {
                throw new InvalidDataException(
                    "삭제할 보관함 항목 종류를 읽을 수 없습니다.");
            }

            lastCapturedAt = DateTimeOffset.Parse(
                reader.GetString(3),
                System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 4; index < 7; index++)
            {
                if (!reader.IsDBNull(index))
                {
                    storedPaths.Add(reader.GetString(index));
                }
            }
        }

        if (!appendSyncDeletion && lastCapturedAt > deletedAt)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using (var lookupMembers = connection.CreateCommand())
        {
            lookupMembers.Transaction = (SqliteTransaction)transaction;
            lookupMembers.CommandText =
                """
                SELECT content_path
                FROM collection_members
                WHERE collection_id = $itemId AND content_path IS NOT NULL;
                """;
            lookupMembers.Parameters.AddWithValue(
                "$itemId",
                resolvedItemId.ToString("D"));
            await using var reader = await lookupMembers.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                storedPaths.Add(reader.GetString(0));
            }
        }

        if (appendSyncDeletion &&
            kind is ContentKind.Url or ContentKind.Image)
        {
            await TryAppendSyncDeletionAsync(
                connection,
                (SqliteTransaction)transaction,
                resolvedItemId,
                deletedAt,
                cancellationToken);
        }

        await DeleteSyncMetadataStateAsync(
            connection,
            (SqliteTransaction)transaction,
            resolvedItemId,
            resolvedNormalizedKey,
            cancellationToken);

        await using (var deleteEvents = connection.CreateCommand())
        {
            deleteEvents.Transaction = (SqliteTransaction)transaction;
            deleteEvents.CommandText =
                "DELETE FROM capture_events WHERE item_id = $itemId;";
            deleteEvents.Parameters.AddWithValue(
                "$itemId",
                resolvedItemId.ToString("D"));
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
                resolvedItemId.ToString("D"));
            affected = await deleteItem.ExecuteNonQueryAsync(cancellationToken);
        }

        await DeleteOrphanOcrAsync(
            connection,
            (SqliteTransaction)transaction,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        if (affected > 0)
        {
            foreach (var storedPath in storedPaths.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                await DeleteStoredFileIfUnreferencedAsync(
                    storedPath,
                    cancellationToken);
            }
        }

        return affected > 0;
    }

    private static async Task DeleteSyncMetadataStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid itemId,
        string normalizedKey,
        CancellationToken cancellationToken)
    {
        await using (var tables = connection.CreateCommand())
        {
            tables.Transaction = transaction;
            tables.CommandText =
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name IN (
                    'sync_item_copy_components',
                    'sync_item_favorite_clock',
                    'sync_item_metadata_exports'
                );
                """;
            if (Convert.ToInt32(
                    await tables.ExecuteScalarAsync(cancellationToken)) != 3)
            {
                return;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM sync_item_copy_components
            WHERE normalized_key = $normalizedKey;
            DELETE FROM sync_item_favorite_clock
            WHERE normalized_key = $normalizedKey;
            DELETE FROM sync_item_metadata_exports
            WHERE item_id = $itemId;
            """;
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TryAppendSyncDeletionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid localItemId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        await using (var tables = connection.CreateCommand())
        {
            tables.Transaction = transaction;
            tables.CommandText =
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name IN (
                    'sync_replica_state',
                    'sync_operations',
                    'sync_item_exports'
                );
                """;
            if (Convert.ToInt32(
                    await tables.ExecuteScalarAsync(cancellationToken)) != 3)
            {
                return;
            }
        }

        Guid syncItemId;
        byte[] payload;
        await using (var source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText =
                """
                SELECT operations.item_id, operations.payload
                FROM sync_operations AS operations
                WHERE operations.kind = $upsertKind
                  AND (
                      operations.operation_id = (
                          SELECT operation_id
                          FROM sync_item_exports
                          WHERE item_id = $itemId
                      ) OR
                      operations.item_id = $itemId OR
                      operations.operation_id IN (
                          SELECT event_id
                          FROM capture_events
                          WHERE item_id = $itemId
                      )
                  )
                ORDER BY CASE WHEN operations.operation_id = (
                             SELECT operation_id
                             FROM sync_item_exports
                             WHERE item_id = $itemId
                         ) THEN 0 ELSE 1 END,
                         julianday(operations.occurred_at) DESC,
                         operations.sequence DESC
                LIMIT 1;
                """;
            source.Parameters.AddWithValue(
                "$upsertKind",
                SyncOperationKind.Upsert.ToString());
            source.Parameters.AddWithValue(
                "$itemId",
                localItemId.ToString("D"));
            await using var reader = await source.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            syncItemId = Guid.Parse(reader.GetString(0));
            payload = (byte[])reader[1];
        }

        string deviceId;
        long sequence;
        await using (var nextSequence = connection.CreateCommand())
        {
            nextSequence.Transaction = transaction;
            nextSequence.CommandText =
                """
                UPDATE sync_replica_state
                SET next_sequence = next_sequence + 1
                WHERE singleton_id = 1
                RETURNING device_id, next_sequence - 1;
                """;
            await using var reader = await nextSequence.ExecuteReaderAsync(
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            deviceId = reader.GetString(0);
            sequence = reader.GetInt64(1);
        }

        var operation = SyncOperation.Create(
            deviceId,
            sequence,
            syncItemId,
            SyncOperationKind.Delete,
            deletedAt,
            payload);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO sync_operations (
                operation_id, device_id, sequence, item_id, kind,
                occurred_at, format_version, encryption_mode,
                payload_sha256, payload, is_published, received_at
            ) VALUES (
                $operationId, $deviceId, $sequence, $itemId, $kind,
                $occurredAt, $formatVersion, $encryptionMode,
                $payloadSha256, $payload, 0, NULL
            );
            """;
        insert.Parameters.AddWithValue(
            "$operationId",
            operation.OperationId.ToString("D"));
        insert.Parameters.AddWithValue("$deviceId", operation.DeviceId);
        insert.Parameters.AddWithValue("$sequence", operation.Sequence);
        insert.Parameters.AddWithValue(
            "$itemId",
            operation.ItemId.ToString("D"));
        insert.Parameters.AddWithValue("$kind", operation.Kind.ToString());
        insert.Parameters.AddWithValue(
            "$occurredAt",
            operation.OccurredAt.ToString("O"));
        insert.Parameters.AddWithValue(
            "$formatVersion",
            operation.FormatVersion);
        insert.Parameters.AddWithValue(
            "$encryptionMode",
            operation.EncryptionMode);
        insert.Parameters.AddWithValue(
            "$payloadSha256",
            operation.PayloadSha256);
        insert.Parameters.AddWithValue("$payload", operation.Payload);
        await insert.ExecuteNonQueryAsync(cancellationToken);
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
                WHERE content_path IS NOT NULL
                UNION
                SELECT content_path
                FROM collection_members
                WHERE content_path IS NOT NULL;
                """;
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
            var isOrphanImage = StoredImageExtensions.Contains(
                                    Path.GetExtension(file)) &&
                                !referencedFiles.Contains(Path.GetFullPath(file));
            if (!isTemporary && !isOrphanImage)
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
                   COALESCE(SUM(CASE WHEN kind = $urlKind THEN 1 ELSE 0 END), 0) +
                       (SELECT COUNT(*) FROM collection_members WHERE kind = $urlKind),
                   COALESCE(SUM(CASE WHEN kind = $imageKind THEN 1 ELSE 0 END), 0) +
                       (SELECT COUNT(*) FROM collection_members WHERE kind = $imageKind),
                   (SELECT COALESCE(SUM(stored_size), 0)
                    FROM (
                        SELECT content_hash, MAX(byte_size) AS stored_size
                        FROM (
                            SELECT content_hash, byte_size
                            FROM items
                            WHERE kind = $imageKind AND content_hash IS NOT NULL
                            UNION ALL
                            SELECT content_hash, byte_size
                            FROM collection_members
                            WHERE kind = $imageKind AND content_hash IS NOT NULL
                        )
                        GROUP BY content_hash
                    ))
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
        var syncDeletionItems = new List<(
            Guid ItemId,
            ContentKind Kind,
            string NormalizedKey)>();
        var imagePaths = new List<string>();
        var previewPaths = new List<string>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"""
                SELECT id, kind, normalized_key, content_path,
                       site_icon_path, preview_image_path
                FROM items
                WHERE {CleanupPredicate}
                """;
            AddCleanupParameters(select, olderThan);
            await using var reader = await select.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Enum.TryParse<ContentKind>(
                        reader.GetString(1),
                        out var kind))
                {
                    throw new InvalidDataException(
                        "정리할 보관함 항목 종류를 읽을 수 없습니다.");
                }

                syncDeletionItems.Add((
                    Guid.Parse(reader.GetString(0)),
                    kind,
                    reader.GetString(2)));
                if (!reader.IsDBNull(3))
                {
                    imagePaths.Add(reader.GetString(3));
                }

                for (var index = 4; index < 6; index++)
                {
                    if (!reader.IsDBNull(index))
                    {
                        previewPaths.Add(reader.GetString(index));
                    }
                }
            }
        }

        await using (var selectMembers = connection.CreateCommand())
        {
            selectMembers.Transaction = transaction;
            selectMembers.CommandText =
                $"""
                SELECT content_path
                FROM collection_members
                WHERE content_path IS NOT NULL AND collection_id IN (
                    SELECT id FROM items WHERE {CleanupPredicate}
                );
                """;
            AddCleanupParameters(selectMembers, olderThan);
            await using var reader = await selectMembers.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                imagePaths.Add(reader.GetString(0));
            }
        }

        var deletedAt = DateTimeOffset.UtcNow;
        foreach (var (itemId, kind, normalizedKey) in syncDeletionItems)
        {
            if (kind is not (ContentKind.Url or ContentKind.Image))
            {
                continue;
            }

            await TryAppendSyncDeletionAsync(
                connection,
                transaction,
                itemId,
                deletedAt,
                cancellationToken);
            await DeleteSyncMetadataStateAsync(
                connection,
                transaction,
                itemId,
                normalizedKey,
                cancellationToken);
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

        await DeleteOrphanOcrAsync(
            connection,
            transaction,
            cancellationToken);

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
                    await DeleteStoredFileIfUnreferencedAsync(
                        relativePath,
                        cancellationToken);
                    if (!File.Exists(target))
                    {
                        deletedImageFiles++;
                    }
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
            SELECT items.id,
                   CASE
                       WHEN items.kind = $collectionKind THEN (
                           SELECT original_url
                           FROM collection_members
                           WHERE collection_id = items.id AND kind = $urlKind
                           ORDER BY position
                           LIMIT 1)
                       ELSE items.original_url
                   END AS preview_url,
                   CASE
                       WHEN items.kind = $collectionKind THEN (
                           SELECT normalized_key
                           FROM collection_members
                           WHERE collection_id = items.id AND kind = $urlKind
                           ORDER BY position
                           LIMIT 1)
                       ELSE items.normalized_key
                   END AS preview_key
            FROM items
            WHERE (items.kind = $urlKind OR
                   (items.kind = $collectionKind AND EXISTS (
                       SELECT 1
                       FROM collection_members
                       WHERE collection_id = items.id AND kind = $urlKind)))
              AND (preview_fetched_at IS NULL OR
                   julianday(preview_fetched_at) < julianday($retryBefore))
            ORDER BY preview_fetched_at IS NOT NULL,
                     last_captured_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$urlKind", ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$collectionKind",
            ContentKind.Collection.ToString());
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
                WHERE id = $itemId AND
                      kind IN ($urlKind, $collectionKind);
                """;
            lookup.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
            lookup.Parameters.AddWithValue(
                "$urlKind",
                ContentKind.Url.ToString());
            lookup.Parameters.AddWithValue(
                "$collectionKind",
                ContentKind.Collection.ToString());
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
            WHERE id = $itemId AND
                  kind IN ($urlKind, $collectionKind);
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
        command.Parameters.AddWithValue("$urlKind", ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$collectionKind",
            ContentKind.Collection.ToString());
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

    private async Task DeleteStoredFileIfUnreferencedAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CASE WHEN
                EXISTS (
                    SELECT 1 FROM items
                    WHERE content_path = $path OR
                          site_icon_path = $path OR
                          preview_image_path = $path
                ) OR EXISTS (
                    SELECT 1 FROM collection_members
                    WHERE content_path = $path
                )
            THEN 1 ELSE 0 END;
            """;
        command.Parameters.AddWithValue("$path", relativePath);
        var referenced = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) != 0;
        if (!referenced)
        {
            DeleteStoredFile(relativePath);
        }
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

    private static async Task DeleteOrphanOcrAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM image_ocr
            WHERE NOT EXISTS (
                      SELECT 1
                      FROM items
                      WHERE lower(items.content_hash) = image_ocr.content_hash)
              AND NOT EXISTS (
                      SELECT 1
                      FROM collection_members
                      WHERE lower(collection_members.content_hash) =
                            image_ocr.content_hash);
            """;
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

    private static async Task<Guid> ResolveItemIdAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid? existingItemId,
        Guid? preferredItemId,
        string normalizedKey,
        CancellationToken cancellationToken)
    {
        if (existingItemId is not null)
        {
            return existingItemId.Value;
        }

        if (preferredItemId is null)
        {
            return Guid.NewGuid();
        }

        if (preferredItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "선호 보관함 항목 ID는 빈 GUID일 수 없습니다.",
                nameof(preferredItemId));
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            SELECT normalized_key
            FROM items
            WHERE id = $itemId;
            """;
        command.Parameters.AddWithValue(
            "$itemId",
            preferredItemId.Value.ToString("D"));
        var existingKey = await command.ExecuteScalarAsync(cancellationToken);
        if (existingKey is string value &&
            !string.Equals(
                value,
                normalizedKey,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "같은 보관함 항목 ID가 다른 콘텐츠에 사용되고 있습니다.");
        }

        return preferredItemId.Value;
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

    private static async Task<int> RecordUsageSessionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        long? latestSessionId = null;
        DateTimeOffset? sessionStartedAt = null;
        DateTimeOffset? lastEventAt = null;
        await using (var latest = connection.CreateCommand())
        {
            latest.Transaction = (SqliteTransaction)transaction;
            latest.CommandText =
                """
                SELECT session_id, session_started_at, last_event_at
                FROM usage_sessions
                WHERE item_id = $itemId
                ORDER BY julianday(last_event_at) DESC
                LIMIT 1;
                """;
            latest.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
            await using var reader =
                await latest.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                latestSessionId = reader.GetInt64(0);
                sessionStartedAt = DateTimeOffset.Parse(
                    reader.GetString(1),
                    System.Globalization.CultureInfo.InvariantCulture);
                lastEventAt = DateTimeOffset.Parse(
                    reader.GetString(2),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        var joinsLatestSession =
            sessionStartedAt is not null &&
            lastEventAt is not null &&
            capturedAt >= sessionStartedAt.Value - UsageSessionGap &&
            capturedAt <= lastEventAt.Value + UsageSessionGap;
        await using (var write = connection.CreateCommand())
        {
            write.Transaction = (SqliteTransaction)transaction;
            if (joinsLatestSession)
            {
                write.CommandText =
                    """
                    UPDATE usage_sessions
                    SET session_started_at = $sessionStartedAt,
                        last_event_at = $lastEventAt
                    WHERE session_id = $sessionId;
                    """;
                write.Parameters.AddWithValue(
                    "$sessionStartedAt",
                    (capturedAt < sessionStartedAt!.Value
                        ? capturedAt
                        : sessionStartedAt.Value).ToString("O"));
                write.Parameters.AddWithValue(
                    "$lastEventAt",
                    (capturedAt > lastEventAt!.Value
                        ? capturedAt
                        : lastEventAt.Value).ToString("O"));
                write.Parameters.AddWithValue(
                    "$sessionId",
                    latestSessionId!.Value);
            }
            else
            {
                write.CommandText =
                    """
                    INSERT INTO usage_sessions (
                        item_id, session_started_at, last_event_at)
                    VALUES ($itemId, $capturedAt, $capturedAt);
                    """;
                write.Parameters.AddWithValue(
                    "$itemId",
                    itemId.ToString("D"));
                write.Parameters.AddWithValue(
                    "$capturedAt",
                    capturedAt.ToString("O"));
            }

            await write.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var count = connection.CreateCommand();
        count.Transaction = (SqliteTransaction)transaction;
        count.CommandText =
            """
            SELECT COUNT(*)
            FROM usage_sessions
            WHERE item_id = $itemId
              AND julianday(last_event_at) >= julianday($cutoff);
            """;
        count.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        count.Parameters.AddWithValue(
            "$cutoff",
            (capturedAt - RecentUsageWindow).ToString("O"));
        return Convert.ToInt32(
            await count.ExecuteScalarAsync(cancellationToken));
    }

    private async Task ApplyAutomaticFavoriteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid itemId,
        ContentKind kind,
        int recentUsageSessionCount,
        CancellationToken cancellationToken)
    {
        var configuration =
            Volatile.Read(ref _autoFavoriteConfiguration);
        if (!configuration.Enabled ||
            recentUsageSessionCount < configuration.UsageThreshold ||
            kind is not (ContentKind.Url or ContentKind.Image))
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
              AND is_favorite = 0;
            """;
        command.Parameters.AddWithValue("$itemId", itemId.ToString("D"));
        command.Parameters.AddWithValue(
            "$changedAt",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task BackfillUsageSessionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            connection,
            """
            DELETE FROM usage_sessions;

            WITH ordered_events AS (
                SELECT item_id,
                       event_id,
                       captured_at,
                       CASE
                           WHEN (
                               julianday(captured_at) -
                               julianday(LAG(captured_at) OVER (
                                   PARTITION BY item_id
                                   ORDER BY julianday(captured_at), event_id))
                           ) * 24.0 > 6.0
                           THEN 1
                           ELSE 0
                       END AS starts_new_session
                FROM capture_events
                WHERE item_id IN (
                    SELECT id
                    FROM items
                    WHERE kind IN ('Url', 'Image'))
            ),
            grouped_events AS (
                SELECT item_id,
                       captured_at,
                       SUM(starts_new_session) OVER (
                           PARTITION BY item_id
                           ORDER BY julianday(captured_at), event_id
                           ROWS UNBOUNDED PRECEDING) AS session_group
                FROM ordered_events
            )
            INSERT INTO usage_sessions (
                item_id, session_started_at, last_event_at)
            SELECT item_id,
                   MIN(captured_at),
                   MAX(captured_at)
            FROM grouped_events
            GROUP BY item_id, session_group;
            """,
            cancellationToken);

    private async Task ApplyAutomaticFavoritesAsync(
        SqliteConnection connection,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        var configuration =
            Volatile.Read(ref _autoFavoriteConfiguration);
        if (!configuration.Enabled)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE items
            SET is_favorite = 1,
                favorite_changed_at = $changedAt
            WHERE kind IN ('Url', 'Image')
              AND is_favorite = 0
              AND id IN (
                  SELECT item_id
                  FROM usage_sessions
                  WHERE julianday(last_event_at) >= julianday($cutoff)
                  GROUP BY item_id
                  HAVING COUNT(*) >= $threshold
              );
            """;
        command.Parameters.AddWithValue(
            "$cutoff",
            (evaluatedAt - RecentUsageWindow).ToString("O"));
        command.Parameters.AddWithValue(
            "$threshold",
            configuration.UsageThreshold);
        command.Parameters.AddWithValue(
            "$changedAt",
            evaluatedAt.ToString("O"));
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
                $id, $kind, $normalizedKey, $originalFileName, '',
                $createdAt, $lastCapturedAt, $lastSourceApp,
                $lastCaptureMethod, $deliveryStatus,
                $captureCount, $shareCount, $contentPath, $contentHash,
                $mimeType, $byteSize, $imageWidth, $imageHeight
            )
            ON CONFLICT(normalized_key) DO UPDATE SET
                original_url = CASE
                    WHEN excluded.original_url <> '' THEN excluded.original_url
                    ELSE items.original_url
                END,
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
            "$originalFileName",
            GetMeaningfulOriginalFileName(request) ?? string.Empty);
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
            request.ContentBytes.Length);
        command.Parameters.AddWithValue("$mimeType", request.MimeType);
        command.Parameters.AddWithValue("$imageWidth", request.PixelWidth);
        command.Parameters.AddWithValue("$imageHeight", request.PixelHeight);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? GetMeaningfulOriginalFileName(
        ImageCaptureRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalFileName))
        {
            return null;
        }

        var fileName = Path.GetFileName(request.OriginalFileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(
            baseName,
            request.Sha256,
            StringComparison.OrdinalIgnoreCase)
            ? null
            : fileName;
    }

    private async Task<string> EnsureImageStoredAsync(
        string sha256,
        string fileExtension,
        ReadOnlyMemory<byte> contentBytes,
        CancellationToken cancellationToken)
    {
        var extension = NormalizeImageExtension(fileExtension);
        var relativePath = Path.Combine("images", $"{sha256.ToLowerInvariant()}{extension}");
        var absolutePath = Path.Combine(paths.RootDirectory, relativePath);
        if (File.Exists(absolutePath))
        {
            return relativePath;
        }

        var temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(
            temporaryPath,
            contentBytes.ToArray(),
            cancellationToken);
        try
        {
            File.Move(temporaryPath, absolutePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(absolutePath))
        {
            File.Delete(temporaryPath);
        }

        return relativePath;
    }

    private static string NormalizeImageExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".png";
        }

        var normalized = extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
        return normalized is ".png" or ".jpg" or ".bmp" or ".gif" or
            ".tif" or ".webp"
            ? normalized
            : ".png";
    }

    private static async Task UpsertCollectionItemAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CollectionCaptureRequest request,
        Guid itemId,
        string normalizedKey,
        int captureCount,
        int shareCount,
        CancellationToken cancellationToken)
    {
        var urls = request.Members
            .Where(member => member.Kind == ContentKind.Url)
            .Select(member => member.OriginalUrl);
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
                $id, $kind, $normalizedKey, $originalUrl, '',
                $createdAt, $lastCapturedAt, $lastSourceApp,
                $lastCaptureMethod, $deliveryStatus,
                $captureCount, $shareCount
            )
            ON CONFLICT(normalized_key) DO UPDATE SET
                original_url = excluded.original_url,
                last_captured_at = excluded.last_captured_at,
                last_source_app = excluded.last_source_app,
                last_capture_method = excluded.last_capture_method,
                delivery_status = excluded.delivery_status,
                capture_count = excluded.capture_count,
                share_count = excluded.share_count;
            """;
        command.Parameters.AddWithValue("$id", itemId.ToString("D"));
        command.Parameters.AddWithValue("$kind", ContentKind.Collection.ToString());
        command.Parameters.AddWithValue("$normalizedKey", normalizedKey);
        command.Parameters.AddWithValue("$originalUrl", string.Join('\n', urls));
        command.Parameters.AddWithValue("$createdAt", request.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastCapturedAt", request.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastSourceApp", request.SourceApp.ToString());
        command.Parameters.AddWithValue("$lastCaptureMethod", request.CaptureMethod.ToString());
        command.Parameters.AddWithValue("$deliveryStatus", request.DeliveryStatus.ToString());
        command.Parameters.AddWithValue("$captureCount", captureCount);
        command.Parameters.AddWithValue("$shareCount", shareCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceCollectionMembersAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        IReadOnlyList<CollectionMemberCaptureRequest> members,
        IReadOnlyDictionary<string, string> storedPaths,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var previousFileNames = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        await using (var previous = connection.CreateCommand())
        {
            previous.Transaction = (SqliteTransaction)transaction;
            previous.CommandText =
                """
                SELECT normalized_key, original_url
                FROM collection_members
                WHERE collection_id = $collectionId
                  AND kind = $imageKind
                  AND original_url <> '';
                """;
            previous.Parameters.AddWithValue(
                "$collectionId",
                collectionId.ToString("D"));
            previous.Parameters.AddWithValue(
                "$imageKind",
                ContentKind.Image.ToString());
            await using var reader = await previous.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                previousFileNames[reader.GetString(0)] = reader.GetString(1);
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText =
                "DELETE FROM collection_members WHERE collection_id = $collectionId;";
            delete.Parameters.AddWithValue("$collectionId", collectionId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText =
                """
                INSERT INTO collection_members (
                    collection_id, position, kind, normalized_key,
                    original_url, domain, content_path, content_hash,
                    mime_type, byte_size, image_width, image_height
                ) VALUES (
                    $collectionId, $position, $kind, $normalizedKey,
                    $originalUrl, $domain, $contentPath, $contentHash,
                    $mimeType, $byteSize, $imageWidth, $imageHeight
                );
                """;
            insert.Parameters.AddWithValue("$collectionId", collectionId.ToString("D"));
            insert.Parameters.AddWithValue("$position", index);
            insert.Parameters.AddWithValue("$kind", member.Kind.ToString());
            insert.Parameters.AddWithValue("$normalizedKey", member.NormalizedKey);
            var originalUrl = member.OriginalUrl;
            if (member.Kind == ContentKind.Image &&
                string.IsNullOrWhiteSpace(originalUrl) &&
                previousFileNames.TryGetValue(
                    member.NormalizedKey,
                    out var previousFileName))
            {
                originalUrl = previousFileName;
            }

            insert.Parameters.AddWithValue("$originalUrl", originalUrl);
            insert.Parameters.AddWithValue("$domain", member.Domain);
            insert.Parameters.AddWithValue(
                "$contentPath",
                member.Kind == ContentKind.Image
                    ? storedPaths[member.NormalizedKey]
                    : DBNull.Value);
            insert.Parameters.AddWithValue("$contentHash", (object?)member.Sha256 ?? DBNull.Value);
            insert.Parameters.AddWithValue("$mimeType", (object?)member.MimeType ?? DBNull.Value);
            insert.Parameters.AddWithValue(
                "$byteSize",
                member.Kind == ContentKind.Image
                    ? member.ContentBytes.Length
                    : DBNull.Value);
            insert.Parameters.AddWithValue("$imageWidth", member.PixelWidth > 0 ? member.PixelWidth : DBNull.Value);
            insert.Parameters.AddWithValue("$imageHeight", member.PixelHeight > 0 ? member.PixelHeight : DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<CapturedCollectionMember>>>
        ReadCollectionMembersAsync(
            SqliteConnection connection,
            IReadOnlyList<Guid> collectionIds,
            CancellationToken cancellationToken)
    {
        var lookup = collectionIds.ToDictionary(
            id => id,
            _ => new List<CapturedCollectionMember>());
        await using var command = connection.CreateCommand();
        var parameterNames = new List<string>(collectionIds.Count);
        for (var index = 0; index < collectionIds.Count; index++)
        {
            var parameterName = $"$id{index}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(
                parameterName,
                collectionIds[index].ToString("D"));
        }

        command.CommandText =
            $"""
            SELECT members.collection_id, members.position, members.kind,
                   members.original_url, members.normalized_key,
                   members.domain, members.content_path,
                   members.content_hash, members.mime_type,
                   members.image_width, members.image_height,
                   image_ocr.display_name, image_ocr.recognized_text,
                   image_ocr.status, image_ocr.language
            FROM collection_members AS members
            LEFT JOIN image_ocr
              ON image_ocr.content_hash = lower(members.content_hash)
            WHERE collection_id IN ({string.Join(',', parameterNames)})
            ORDER BY collection_id, position;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var collectionId = Guid.Parse(reader.GetString(0));
            lookup[collectionId].Add(new CapturedCollectionMember(
                reader.GetInt32(1),
                Enum.Parse<ContentKind>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13)
                    ? null
                    : Enum.Parse<ImageOcrStatus>(reader.GetString(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return lookup.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CapturedCollectionMember>)pair.Value);
    }

    private sealed record AutoFavoriteConfiguration(
        bool Enabled,
        int UsageThreshold);
}
