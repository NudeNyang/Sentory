namespace Sentory.Core.Sync;

public sealed record SyncObjectInfo(
    string Key,
    long Size,
    string Sha256);

public sealed record SyncObjectPage(
    IReadOnlyList<SyncObjectInfo> Items,
    string? ContinuationToken);

public sealed record SyncStoredObject(
    string Key,
    string Sha256,
    byte[] Content);

public enum SyncPutResult
{
    Created,
    AlreadyExists
}

public interface ISyncObjectStore
{
    Task<SyncObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SyncPutResult> PutIfAbsentAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken = default);

    Task<SyncStoredObject?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);
}

public sealed class SyncStoreUnavailableException(
    string message,
    Exception? innerException = null)
    : IOException(message, innerException);

public static class SyncOperationObjectKey
{
    public const string OperationsPrefix = "devices/";

    public static string Create(SyncOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return string.Concat(
            "devices/",
            operation.DeviceId,
            "/operations/",
            operation.Sequence.ToString(
                "D20",
                System.Globalization.CultureInfo.InvariantCulture),
            "-",
            operation.OperationId.ToString("N"),
            ".json");
    }

    public static bool TryParse(
        string? key,
        out string deviceId,
        out long sequence,
        out Guid operationId)
    {
        deviceId = string.Empty;
        sequence = 0;
        operationId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('/');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "devices", StringComparison.Ordinal) ||
            !string.Equals(parts[2], "operations", StringComparison.Ordinal) ||
            !SyncDeviceIdentity.IsValid(parts[1]))
        {
            return false;
        }

        const int sequenceLength = 20;
        const int separatorLength = 1;
        const int operationIdLength = 32;
        const string extension = ".json";
        var fileName = parts[3];
        if (fileName.Length !=
            sequenceLength + separatorLength + operationIdLength +
            extension.Length ||
            fileName[sequenceLength] != '-' ||
            !fileName.EndsWith(extension, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(
                fileName.AsSpan(0, sequenceLength),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out sequence) ||
            sequence <= 0 ||
            !Guid.TryParseExact(
                fileName.AsSpan(
                    sequenceLength + separatorLength,
                    operationIdLength),
                "N",
                out operationId))
        {
            sequence = 0;
            operationId = Guid.Empty;
            return false;
        }

        deviceId = parts[1];
        return true;
    }
}
