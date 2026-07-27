using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Data;

public sealed class SyncDeviceBindingMismatchException()
    : InvalidOperationException(
        "이 데이터베이스는 다른 동기화 기기 ID에 연결되어 있습니다.");

public sealed class SqliteSyncOperationJournal :
    ISyncOperationJournal,
    ISyncItemExportJournal
{
    private readonly string _connectionString;

    public SqliteSyncOperationJournal(
        SentoryDataPaths paths,
        string deviceId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!SyncDeviceIdentity.IsValid(deviceId))
        {
            throw new ArgumentException(
                "동기화 기기 ID 형식이 올바르지 않습니다.",
                nameof(deviceId));
        }

        Paths = paths;
        DeviceId = deviceId;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public SentoryDataPaths Paths { get; }

    public string DeviceId { get; }

    public static async Task<string?> GetBoundDeviceIdAsync(
        SentoryDataPaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!File.Exists(paths.DatabasePath))
        {
            return null;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var table = connection.CreateCommand();
        table.CommandText =
            """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'sync_replica_state';
            """;
        if (await table.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }

        await using var state = connection.CreateCommand();
        state.CommandText =
            """
            SELECT device_id
            FROM sync_replica_state
            WHERE singleton_id = 1;
            """;
        return Convert.ToString(
            await state.ExecuteScalarAsync(cancellationToken));
    }

    public static async Task ResetForNewStoreAsync(
        SentoryDataPaths paths,
        string newDeviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!SyncDeviceIdentity.IsValid(newDeviceId))
        {
            throw new ArgumentException(
                "새 동기화 기기 ID 형식이 올바르지 않습니다.",
                nameof(newDeviceId));
        }

        paths.EnsureDirectories();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        await using (var connection = new SqliteConnection(
                         connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA busy_timeout = 5000;

                DROP TABLE IF EXISTS sync_item_exports;
                DROP TABLE IF EXISTS sync_item_metadata_exports;
                DROP TABLE IF EXISTS sync_item_copy_components;
                DROP TABLE IF EXISTS sync_item_favorite_clock;
                DROP TABLE IF EXISTS sync_metadata_applied;
                DROP TABLE IF EXISTS sync_auto_favorite_clock;
                DROP TABLE IF EXISTS sync_checkpoints;
                DROP TABLE IF EXISTS sync_operations;
                DROP TABLE IF EXISTS sync_replica_state;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var journal = new SqliteSyncOperationJournal(
            paths,
            newDeviceId);
        await journal.InitializeAsync(cancellationToken);
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        Paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sync_replica_state (
                singleton_id INTEGER NOT NULL PRIMARY KEY
                    CHECK(singleton_id = 1),
                device_id TEXT NOT NULL,
                next_sequence INTEGER NOT NULL
                    CHECK(next_sequence > 0)
            );

            CREATE TABLE IF NOT EXISTS sync_operations (
                operation_id TEXT NOT NULL PRIMARY KEY,
                device_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                item_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                occurred_at TEXT NOT NULL,
                format_version INTEGER NOT NULL,
                encryption_mode TEXT NOT NULL,
                payload_sha256 TEXT NOT NULL,
                payload BLOB NOT NULL,
                is_published INTEGER NOT NULL,
                received_at TEXT NULL,
                UNIQUE(device_id, sequence)
            );

            CREATE TABLE IF NOT EXISTS sync_checkpoints (
                remote_device_id TEXT NOT NULL PRIMARY KEY,
                last_sequence INTEGER NOT NULL
                    CHECK(last_sequence >= 0),
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_item_exports (
                item_id TEXT NOT NULL PRIMARY KEY,
                last_captured_at TEXT NOT NULL,
                operation_id TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_item_copy_components (
                normalized_key TEXT NOT NULL,
                device_id TEXT NOT NULL,
                item_captured_at TEXT NOT NULL,
                copy_count INTEGER NOT NULL CHECK(copy_count >= 0),
                last_copied_at TEXT NULL,
                PRIMARY KEY(normalized_key, device_id)
            );

            CREATE TABLE IF NOT EXISTS sync_item_favorite_clock (
                normalized_key TEXT NOT NULL PRIMARY KEY,
                is_favorite INTEGER NOT NULL,
                changed_at TEXT NOT NULL,
                device_id TEXT NOT NULL,
                sequence INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_item_metadata_exports (
                item_id TEXT NOT NULL PRIMARY KEY,
                fingerprint TEXT NOT NULL,
                operation_id TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_metadata_applied (
                operation_id TEXT NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_auto_favorite_clock (
                singleton_id INTEGER NOT NULL PRIMARY KEY
                    CHECK(singleton_id = 1),
                enabled INTEGER NOT NULL,
                usage_threshold INTEGER NOT NULL,
                changed_at TEXT NOT NULL,
                device_id TEXT NOT NULL,
                sequence INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sync_operations_publish
                ON sync_operations(
                    device_id,
                    is_published,
                    sequence);
            CREATE INDEX IF NOT EXISTS ix_sync_operations_received
                ON sync_operations(
                    received_at,
                    device_id,
                    sequence);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var initializeState = connection.CreateCommand();
        initializeState.CommandText =
            """
            INSERT OR IGNORE INTO sync_replica_state (
                singleton_id,
                device_id,
                next_sequence
            ) VALUES (1, $deviceId, 1);
            """;
        initializeState.Parameters.AddWithValue("$deviceId", DeviceId);
        await initializeState.ExecuteNonQueryAsync(cancellationToken);

        await using var readState = connection.CreateCommand();
        readState.CommandText =
            """
            SELECT device_id
            FROM sync_replica_state
            WHERE singleton_id = 1;
            """;
        var storedDeviceId = Convert.ToString(
            await readState.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(
                storedDeviceId,
                DeviceId,
                StringComparison.Ordinal))
        {
            throw new SyncDeviceBindingMismatchException();
        }
    }

    public async Task<SyncOperation> AppendLocalAsync(
        Guid itemId,
        SyncOperationKind kind,
        DateTimeOffset occurredAt,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        long sequence;
        await using (var nextSequence = connection.CreateCommand())
        {
            nextSequence.Transaction = (SqliteTransaction)transaction;
            nextSequence.CommandText =
                """
                UPDATE sync_replica_state
                SET next_sequence = next_sequence + 1
                WHERE singleton_id = 1
                  AND device_id = $deviceId
                RETURNING next_sequence - 1;
                """;
            nextSequence.Parameters.AddWithValue("$deviceId", DeviceId);
            var value = await nextSequence.ExecuteScalarAsync(
                cancellationToken);
            if (value is null)
            {
                throw new InvalidOperationException(
                    "동기화 저널을 먼저 초기화해야 합니다.");
            }

            sequence = Convert.ToInt64(value);
        }

        var operation = SyncOperation.Create(
            DeviceId,
            sequence,
            itemId,
            kind,
            occurredAt,
            payload.Span);
        await InsertOperationAsync(
            connection,
            transaction,
            operation,
            isPublished: false,
            receivedAt: null,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return operation;
    }

    public async Task<IReadOnlyList<SyncOperation>> GetUnpublishedAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT format_version, encryption_mode, operation_id,
                   device_id, sequence, item_id, kind, occurred_at,
                   payload_sha256, payload
            FROM sync_operations
            WHERE device_id = $deviceId
              AND is_published = 0
            ORDER BY sequence
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$deviceId", DeviceId);
        command.Parameters.AddWithValue("$limit", limit);
        return await ReadOperationsAsync(command, cancellationToken);
    }

    public async Task MarkPublishedAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "동기화 작업 ID가 필요합니다.",
                nameof(operationId));
        }

        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE sync_operations
            SET is_published = 1
            WHERE operation_id = $operationId
              AND device_id = $deviceId;
            """;
        command.Parameters.AddWithValue(
            "$operationId",
            operationId.ToString("D"));
        command.Parameters.AddWithValue("$deviceId", DeviceId);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0)
        {
            throw new InvalidOperationException(
                "게시할 로컬 동기화 작업을 찾을 수 없습니다.");
        }
    }

    public async Task<SyncCheckpoint> GetCheckpointAsync(
        string remoteDeviceId,
        CancellationToken cancellationToken = default)
    {
        ValidateRemoteDeviceId(remoteDeviceId);
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT last_sequence, updated_at
            FROM sync_checkpoints
            WHERE remote_device_id = $remoteDeviceId;
            """;
        command.Parameters.AddWithValue(
            "$remoteDeviceId",
            remoteDeviceId);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SyncCheckpoint(remoteDeviceId, 0, null);
        }

        return new SyncCheckpoint(
            remoteDeviceId,
            reader.GetInt64(0),
            DateTimeOffset.Parse(
                reader.GetString(1),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<SyncApplyResult> ApplyRemoteAsync(
        SyncOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateRemoteDeviceId(operation.DeviceId);
        if (!operation.HasValidPayloadHash())
        {
            throw new InvalidDataException(
                "원격 동기화 작업 본문의 SHA-256이 일치하지 않습니다.");
        }

        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);

        var existing = await ReadOperationIdentityAsync(
                connection,
                transaction,
                operation.OperationId,
                cancellationToken);
        if (existing is not null)
        {
            if (!existing.Matches(operation))
            {
                throw new InvalidDataException(
                    "같은 동기화 작업 ID에 서로 다른 내용이 있습니다.");
            }

            await transaction.CommitAsync(cancellationToken);
            return SyncApplyResult.AlreadyApplied;
        }

        var checkpoint = await GetCheckpointAsync(
            connection,
            transaction,
            operation.DeviceId,
            cancellationToken);
        if (operation.Sequence <= checkpoint)
        {
            await transaction.CommitAsync(cancellationToken);
            return SyncApplyResult.AlreadyApplied;
        }

        if (operation.Sequence != checkpoint + 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return SyncApplyResult.SequenceGap;
        }

        var appliedAt = DateTimeOffset.UtcNow;
        await InsertOperationAsync(
            connection,
            transaction,
            operation,
            isPublished: true,
            receivedAt: appliedAt,
            cancellationToken);
        await using (var updateCheckpoint = connection.CreateCommand())
        {
            updateCheckpoint.Transaction = (SqliteTransaction)transaction;
            updateCheckpoint.CommandText =
                """
                INSERT INTO sync_checkpoints (
                    remote_device_id,
                    last_sequence,
                    updated_at
                ) VALUES (
                    $remoteDeviceId,
                    $lastSequence,
                    $updatedAt
                )
                ON CONFLICT(remote_device_id) DO UPDATE SET
                    last_sequence = excluded.last_sequence,
                    updated_at = excluded.updated_at;
                """;
            updateCheckpoint.Parameters.AddWithValue(
                "$remoteDeviceId",
                operation.DeviceId);
            updateCheckpoint.Parameters.AddWithValue(
                "$lastSequence",
                operation.Sequence);
            updateCheckpoint.Parameters.AddWithValue(
                "$updatedAt",
                appliedAt.ToString("O"));
            await updateCheckpoint.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return SyncApplyResult.Applied;
    }

    public async Task<IReadOnlyList<SyncOperation>> GetReceivedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT format_version, encryption_mode, operation_id,
                   device_id, sequence, item_id, kind, occurred_at,
                   payload_sha256, payload
            FROM sync_operations
            WHERE received_at IS NOT NULL
            ORDER BY received_at, device_id, sequence;
            """;
        return await ReadOperationsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<SyncOperation>> GetDeletionOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT format_version, encryption_mode, operation_id,
                   device_id, sequence, item_id, kind, occurred_at,
                   payload_sha256, payload
            FROM sync_operations
            WHERE kind = $deleteKind
            ORDER BY julianday(occurred_at), device_id, sequence;
            """;
        command.Parameters.AddWithValue(
            "$deleteKind",
            SyncOperationKind.Delete.ToString());
        return await ReadOperationsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<SyncItemExportCandidate>>
        GetPendingItemExportsAsync(
            int limit,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT items.id, items.kind, items.original_url,
                   items.normalized_key, items.domain,
                   items.last_source_app, items.last_capture_method,
                   items.delivery_status, items.created_at,
                   items.last_captured_at, items.content_path,
                   items.content_hash, items.mime_type,
                   items.image_width, items.image_height
            FROM items
            LEFT JOIN sync_item_exports
              ON sync_item_exports.item_id = items.id
            WHERE items.kind IN ($urlKind, $imageKind)
              AND (
                  sync_item_exports.item_id IS NULL OR
                  sync_item_exports.last_captured_at <>
                      items.last_captured_at
              )
            ORDER BY julianday(items.last_captured_at), items.id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$urlKind",
            ContentKind.Url.ToString());
        command.Parameters.AddWithValue(
            "$imageKind",
            ContentKind.Image.ToString());
        command.Parameters.AddWithValue("$limit", limit);

        var values = new List<SyncItemExportCandidate>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<ContentKind>(
                    reader.GetString(1),
                    out var kind) ||
                !Enum.TryParse<SourceApp>(
                    reader.GetString(5),
                    out var sourceApp) ||
                !Enum.TryParse<CaptureMethod>(
                    reader.GetString(6),
                    out var captureMethod) ||
                !Enum.TryParse<DeliveryStatus>(
                    reader.GetString(7),
                    out var deliveryStatus))
            {
                throw new InvalidDataException(
                    "동기화할 보관함 항목 값을 읽을 수 없습니다.");
            }

            values.Add(new SyncItemExportCandidate(
                Guid.Parse(reader.GetString(0)),
                kind,
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                sourceApp,
                captureMethod,
                deliveryStatus,
                DateTimeOffset.Parse(
                    reader.GetString(8),
                    System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(
                    reader.GetString(9),
                    System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                reader.IsDBNull(14) ? null : reader.GetInt32(14)));
        }

        return values;
    }

    public async Task<SyncOperation?> AppendLocalItemExportAsync(
        SyncItemExportCandidate candidate,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);

        await using (var currentItem = connection.CreateCommand())
        {
            currentItem.Transaction = (SqliteTransaction)transaction;
            currentItem.CommandText =
                """
                SELECT last_captured_at
                FROM items
                WHERE id = $itemId;
                """;
            currentItem.Parameters.AddWithValue(
                "$itemId",
                candidate.ItemId.ToString("D"));
            var value = Convert.ToString(
                await currentItem.ExecuteScalarAsync(cancellationToken));
            if (!string.Equals(
                    value,
                    candidate.LastCapturedAt.ToString("O"),
                    StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
        }

        await using (var existingExport = connection.CreateCommand())
        {
            existingExport.Transaction = (SqliteTransaction)transaction;
            existingExport.CommandText =
                """
                SELECT last_captured_at
                FROM sync_item_exports
                WHERE item_id = $itemId;
                """;
            existingExport.Parameters.AddWithValue(
                "$itemId",
                candidate.ItemId.ToString("D"));
            var value = Convert.ToString(
                await existingExport.ExecuteScalarAsync(cancellationToken));
            if (string.Equals(
                    value,
                    candidate.LastCapturedAt.ToString("O"),
                    StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
        }

        var sequence = await TakeNextSequenceAsync(
            connection,
            transaction,
            cancellationToken);
        var operation = SyncOperation.Create(
            DeviceId,
            sequence,
            candidate.ItemId,
            SyncOperationKind.Upsert,
            candidate.LastCapturedAt,
            payload.Span);
        await InsertOperationAsync(
            connection,
            transaction,
            operation,
            isPublished: false,
            receivedAt: null,
            cancellationToken);
        await using (var markExported = connection.CreateCommand())
        {
            markExported.Transaction = (SqliteTransaction)transaction;
            markExported.CommandText =
                """
                INSERT INTO sync_item_exports (
                    item_id,
                    last_captured_at,
                    operation_id
                ) VALUES (
                    $itemId,
                    $lastCapturedAt,
                    $operationId
                )
                ON CONFLICT(item_id) DO UPDATE SET
                    last_captured_at = excluded.last_captured_at,
                    operation_id = excluded.operation_id;
                """;
            markExported.Parameters.AddWithValue(
                "$itemId",
                candidate.ItemId.ToString("D"));
            markExported.Parameters.AddWithValue(
                "$lastCapturedAt",
                candidate.LastCapturedAt.ToString("O"));
            markExported.Parameters.AddWithValue(
                "$operationId",
                operation.OperationId.ToString("D"));
            await markExported.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return operation;
    }

    public async Task MarkRemoteItemProjectedAsync(
        Guid localItemId,
        DateTimeOffset lastCapturedAt,
        Guid remoteOperationId,
        CancellationToken cancellationToken = default)
    {
        if (localItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "로컬 보관함 항목 ID가 필요합니다.",
                nameof(localItemId));
        }

        if (remoteOperationId == Guid.Empty)
        {
            throw new ArgumentException(
                "원격 동기화 작업 ID가 필요합니다.",
                nameof(remoteOperationId));
        }

        await using var connection = await OpenConnectionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sync_item_exports (
                item_id,
                last_captured_at,
                operation_id
            )
            SELECT $itemId, $lastCapturedAt, $operationId
            WHERE EXISTS (
                SELECT 1
                FROM items
                WHERE id = $itemId
                  AND last_captured_at = $lastCapturedAt
            )
            ON CONFLICT(item_id) DO UPDATE SET
                last_captured_at = excluded.last_captured_at,
                operation_id = excluded.operation_id;
            """;
        command.Parameters.AddWithValue(
            "$itemId",
            localItemId.ToString("D"));
        command.Parameters.AddWithValue(
            "$lastCapturedAt",
            lastCapturedAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$operationId",
            remoteOperationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private async Task<long> TakeNextSequenceAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var nextSequence = connection.CreateCommand();
        nextSequence.Transaction = (SqliteTransaction)transaction;
        nextSequence.CommandText =
            """
            UPDATE sync_replica_state
            SET next_sequence = next_sequence + 1
            WHERE singleton_id = 1
              AND device_id = $deviceId
            RETURNING next_sequence - 1;
            """;
        nextSequence.Parameters.AddWithValue("$deviceId", DeviceId);
        var value = await nextSequence.ExecuteScalarAsync(
            cancellationToken);
        if (value is null)
        {
            throw new InvalidOperationException(
                "동기화 저널을 먼저 초기화해야 합니다.");
        }

        return Convert.ToInt64(value);
    }

    private static async Task InsertOperationAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        SyncOperation operation,
        bool isPublished,
        DateTimeOffset? receivedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO sync_operations (
                operation_id, device_id, sequence, item_id, kind,
                occurred_at, format_version, encryption_mode,
                payload_sha256, payload, is_published, received_at
            ) VALUES (
                $operationId, $deviceId, $sequence, $itemId, $kind,
                $occurredAt, $formatVersion, $encryptionMode,
                $payloadSha256, $payload, $isPublished, $receivedAt
            );
            """;
        command.Parameters.AddWithValue(
            "$operationId",
            operation.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$deviceId", operation.DeviceId);
        command.Parameters.AddWithValue("$sequence", operation.Sequence);
        command.Parameters.AddWithValue(
            "$itemId",
            operation.ItemId.ToString("D"));
        command.Parameters.AddWithValue("$kind", operation.Kind.ToString());
        command.Parameters.AddWithValue(
            "$occurredAt",
            operation.OccurredAt.ToString("O"));
        command.Parameters.AddWithValue(
            "$formatVersion",
            operation.FormatVersion);
        command.Parameters.AddWithValue(
            "$encryptionMode",
            operation.EncryptionMode);
        command.Parameters.AddWithValue(
            "$payloadSha256",
            operation.PayloadSha256);
        command.Parameters.AddWithValue("$payload", operation.Payload);
        command.Parameters.AddWithValue(
            "$isPublished",
            isPublished ? 1 : 0);
        command.Parameters.AddWithValue(
            "$receivedAt",
            receivedAt is null
                ? DBNull.Value
                : receivedAt.Value.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<StoredOperationIdentity?>
        ReadOperationIdentityAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            SELECT device_id, sequence, item_id, kind, occurred_at,
                   format_version, encryption_mode, payload_sha256
            FROM sync_operations
            WHERE operation_id = $operationId;
            """;
        command.Parameters.AddWithValue(
            "$operationId",
            operationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!Enum.TryParse<SyncOperationKind>(
                reader.GetString(3),
                out var kind))
        {
            throw new InvalidDataException(
                "저장된 동기화 작업 종류를 읽을 수 없습니다.");
        }

        return new StoredOperationIdentity(
            reader.GetString(0),
            reader.GetInt64(1),
            Guid.Parse(reader.GetString(2)),
            kind,
            DateTimeOffset.Parse(
                reader.GetString(4),
                System.Globalization.CultureInfo.InvariantCulture),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7));
    }

    private static async Task<long> GetCheckpointAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string remoteDeviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            SELECT last_sequence
            FROM sync_checkpoints
            WHERE remote_device_id = $remoteDeviceId;
            """;
        command.Parameters.AddWithValue(
            "$remoteDeviceId",
            remoteDeviceId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? 0 : Convert.ToInt64(value);
    }

    private static async Task<IReadOnlyList<SyncOperation>>
        ReadOperationsAsync(
            SqliteCommand command,
            CancellationToken cancellationToken)
    {
        var values = new List<SyncOperation>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<SyncOperationKind>(
                    reader.GetString(6),
                    out var kind))
            {
                throw new InvalidDataException(
                    "저장된 동기화 작업 종류를 읽을 수 없습니다.");
            }

            values.Add(new SyncOperation(
                reader.GetInt32(0),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetInt64(4),
                Guid.Parse(reader.GetString(5)),
                kind,
                DateTimeOffset.Parse(
                    reader.GetString(7),
                    System.Globalization.CultureInfo.InvariantCulture),
                reader.GetString(8),
                (byte[])reader[9]));
        }

        return values;
    }

    private void ValidateRemoteDeviceId(string remoteDeviceId)
    {
        if (!SyncDeviceIdentity.IsValid(remoteDeviceId))
        {
            throw new ArgumentException(
                "원격 동기화 기기 ID 형식이 올바르지 않습니다.",
                nameof(remoteDeviceId));
        }

        if (string.Equals(
                remoteDeviceId,
                DeviceId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "현재 기기를 원격 동기화 기기로 사용할 수 없습니다.",
                nameof(remoteDeviceId));
        }
    }

    private sealed record StoredOperationIdentity(
        string DeviceId,
        long Sequence,
        Guid ItemId,
        SyncOperationKind Kind,
        DateTimeOffset OccurredAt,
        int FormatVersion,
        string EncryptionMode,
        string PayloadSha256)
    {
        public bool Matches(SyncOperation operation) =>
            string.Equals(
                DeviceId,
                operation.DeviceId,
                StringComparison.Ordinal) &&
            Sequence == operation.Sequence &&
            ItemId == operation.ItemId &&
            Kind == operation.Kind &&
            OccurredAt == operation.OccurredAt &&
            FormatVersion == operation.FormatVersion &&
            string.Equals(
                EncryptionMode,
                operation.EncryptionMode,
                StringComparison.Ordinal) &&
            string.Equals(
                PayloadSha256,
                operation.PayloadSha256,
                StringComparison.OrdinalIgnoreCase);
    }
}
