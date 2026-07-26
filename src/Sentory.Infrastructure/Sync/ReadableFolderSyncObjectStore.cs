using System.Security.Cryptography;
using System.Text;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Sync;

public sealed class ReadableFolderSyncObjectStore : IReadableSyncObjectStore
{
    private const long MaximumPhotoBytes = 100L * 1024 * 1024;
    private readonly string _selectedDirectory;
    private readonly string _photosDirectory;
    private readonly string _linksDirectory;
    private readonly string _temporaryDirectory;
    private readonly LocalFolderSyncObjectStore _operationStore;
    private readonly LocalFolderSyncObjectStore _legacyStore;

    public ReadableFolderSyncObjectStore(string selectedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedDirectory);
        _selectedDirectory = Path.GetFullPath(selectedDirectory);
        _photosDirectory = Path.Combine(_selectedDirectory, "Photos");
        _linksDirectory = Path.Combine(_selectedDirectory, "Links");
        _temporaryDirectory = Path.Combine(
            _selectedDirectory,
            ".sentory",
            "v2",
            "temporary-content");
        _operationStore = new LocalFolderSyncObjectStore(
            _selectedDirectory,
            Path.Combine(".sentory", "v2"));
        _legacyStore = new LocalFolderSyncObjectStore(_selectedDirectory);
    }

    public string PhotosDirectory => _photosDirectory;

    public string LinksDirectory => _linksDirectory;

    public string InternalStoreDirectory => _operationStore.StoreDirectory;

    public string CreateImageObjectKey(
        string sha256,
        string fileExtension) =>
        SyncBlobObjectKey.CreateReadable(sha256, fileExtension);

    public Task<SyncObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        _operationStore.ListAsync(
            prefix,
            continuationToken,
            pageSize,
            cancellationToken);

    public async Task<SyncPutResult> PutIfAbsentAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        if (SyncBlobObjectKey.TryParseReadable(
                key,
                out var contentSha256,
                out var fileExtension))
        {
            return await PutPhotoIfAbsentAsync(
                key,
                contentSha256,
                fileExtension,
                content,
                sha256,
                cancellationToken);
        }

        if (SyncOperationObjectKey.TryParse(
                key,
                out _,
                out _,
                out _))
        {
            try
            {
                await MaterializeReadableLinkAsync(
                    content,
                    cancellationToken);
            }
            catch (Exception exception) when (IsStoreUnavailable(exception))
            {
                throw CreateUnavailableException(exception);
            }

            return await _operationStore.PutIfAbsentAsync(
                key,
                content,
                sha256,
                cancellationToken);
        }

        return await _legacyStore.PutIfAbsentAsync(
            key,
            content,
            sha256,
            cancellationToken);
    }

    public async Task<SyncStoredObject?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (SyncBlobObjectKey.TryParseReadable(
                key,
                out var sha256,
                out var fileExtension))
        {
            return await TryGetPhotoAsync(
                key,
                sha256,
                fileExtension,
                cancellationToken);
        }

        if (SyncOperationObjectKey.TryParse(
                key,
                out _,
                out _,
                out _))
        {
            return await _operationStore.TryGetAsync(
                key,
                cancellationToken);
        }

        return await _legacyStore.TryGetAsync(key, cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (SyncBlobObjectKey.TryParseReadable(
                key,
                out var sha256,
                out var fileExtension))
        {
            return await TryGetPhotoAsync(
                key,
                sha256,
                fileExtension,
                cancellationToken) is not null;
        }

        if (SyncOperationObjectKey.TryParse(
                key,
                out _,
                out _,
                out _))
        {
            return await _operationStore.ExistsAsync(
                key,
                cancellationToken);
        }

        return await _legacyStore.ExistsAsync(key, cancellationToken);
    }

    internal string GetPhotoPathForTesting(
        string sha256,
        string fileExtension) =>
        GetPhotoPath(sha256, fileExtension);

    private async Task<SyncPutResult> PutPhotoIfAbsentAsync(
        string key,
        string contentSha256,
        string fileExtension,
        ReadOnlyMemory<byte> content,
        string declaredSha256,
        CancellationToken cancellationToken)
    {
        if (content.Length <= 0 || content.Length > MaximumPhotoBytes)
        {
            throw new InvalidDataException(
                "클라우드에 저장할 사진 크기가 허용 범위를 벗어났습니다.");
        }

        var actualSha256 = ComputeSha256(content.Span);
        if (!string.Equals(
                contentSha256,
                actualSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                declaredSha256,
                actualSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "사진 키·내용과 SHA-256이 일치하지 않습니다.");
        }

        EnsureReadableDirectories();
        var targetPath = GetPhotoPath(contentSha256, fileExtension);
        EnsureNotSymbolicLinkIfPresent(targetPath);
        if (File.Exists(targetPath))
        {
            var existing = await ReadAndValidatePhotoAsync(
                targetPath,
                contentSha256,
                cancellationToken);
            if (!existing.AsSpan().SequenceEqual(content.Span))
            {
                throw new InvalidDataException(
                    $"같은 사진 키에 다른 내용이 있습니다: {key}");
            }

            return SyncPutResult.AlreadyExists;
        }

        var temporaryPath = Path.Combine(
            _temporaryDirectory,
            $"{Guid.NewGuid():N}.partial");
        try
        {
            await WriteAllBytesDurablyAsync(
                temporaryPath,
                content,
                cancellationToken);
            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
                return SyncPutResult.Created;
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                var existing = await ReadAndValidatePhotoAsync(
                    targetPath,
                    contentSha256,
                    cancellationToken);
                if (!existing.AsSpan().SequenceEqual(content.Span))
                {
                    throw new InvalidDataException(
                        $"같은 사진 키에 다른 내용이 있습니다: {key}");
                }

                return SyncPutResult.AlreadyExists;
            }
        }
        catch (Exception exception) when (IsStoreUnavailable(exception))
        {
            throw CreateUnavailableException(exception);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task<SyncStoredObject?> TryGetPhotoAsync(
        string key,
        string sha256,
        string fileExtension,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = GetPhotoPath(sha256, fileExtension);
            if (!File.Exists(path))
            {
                return null;
            }

            EnsureNotSymbolicLinkIfPresent(path);
            var content = await ReadAndValidatePhotoAsync(
                path,
                sha256,
                cancellationToken);
            return new SyncStoredObject(key, sha256, content);
        }
        catch (Exception exception) when (IsStoreUnavailable(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    private async Task MaterializeReadableLinkAsync(
        ReadOnlyMemory<byte> operationContent,
        CancellationToken cancellationToken)
    {
        SyncOperation operation;
        SyncItemPayload payload;
        try
        {
            operation = SyncOperationSerializer.Deserialize(
                operationContent.Span);
            payload = SyncItemPayloadSerializer.Deserialize(
                operation.Payload);
        }
        catch (Exception exception)
            when (exception is InvalidDataException or
                  NotSupportedException)
        {
            return;
        }

        if (operation.Kind != SyncOperationKind.Upsert ||
            payload.Url is not { } url)
        {
            return;
        }

        EnsureReadableDirectories();
        var capturedAt = payload.CapturedAt;
        var directory = Path.Combine(
            _linksDirectory,
            capturedAt.Year.ToString("D4"),
            capturedAt.Month.ToString("D2"));
        EnsureManagedDirectory(directory);
        var domain = SanitizeFileNamePart(url.Domain, "link");
        var fileName = string.Concat(
            capturedAt.ToString("yyyy-MM-dd_HHmmss"),
            "_",
            domain,
            "_",
            operation.OperationId.ToString("N"),
            ".txt");
        var targetPath = Path.Combine(directory, fileName);
        EnsureNotSymbolicLinkIfPresent(targetPath);
        var text = string.Join(
            Environment.NewLine,
            $"주소: {url.OriginalUrl}",
            $"도메인: {url.Domain}",
            $"저장 시각: {capturedAt:O}",
            $"출처: {payload.SourceApp}",
            string.Empty);
        var content = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        await PutReadableFileIfAbsentAsync(
            targetPath,
            content,
            cancellationToken);
    }

    private async Task PutReadableFileIfAbsentAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            var existing = await File.ReadAllBytesAsync(
                targetPath,
                cancellationToken);
            if (!existing.AsSpan().SequenceEqual(content.Span))
            {
                throw new InvalidDataException(
                    "같은 링크 파일 이름에 다른 내용이 있습니다.");
            }

            return;
        }

        var temporaryPath = Path.Combine(
            _temporaryDirectory,
            $"{Guid.NewGuid():N}.partial");
        try
        {
            await WriteAllBytesDurablyAsync(
                temporaryPath,
                content,
                cancellationToken);
            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                var existing = await File.ReadAllBytesAsync(
                    targetPath,
                    cancellationToken);
                if (!existing.AsSpan().SequenceEqual(content.Span))
                {
                    throw new InvalidDataException(
                        "같은 링크 파일 이름에 다른 내용이 있습니다.");
                }
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private string GetPhotoPath(
        string sha256,
        string fileExtension) =>
        Path.Combine(
            _photosDirectory,
            string.Concat(
                sha256,
                SyncBlobObjectKey.NormalizeReadableExtension(
                    fileExtension)));

    private void EnsureReadableDirectories()
    {
        Directory.CreateDirectory(_selectedDirectory);
        EnsureManagedDirectory(_photosDirectory);
        EnsureManagedDirectory(_linksDirectory);
        EnsureManagedDirectory(Path.Combine(
            _selectedDirectory,
            ".sentory"));
        EnsureManagedDirectory(Path.Combine(
            _selectedDirectory,
            ".sentory",
            "v2"));
        EnsureManagedDirectory(_temporaryDirectory);
    }

    private static void EnsureManagedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null ||
            (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "동기화 관리 폴더는 심볼릭 링크일 수 없습니다.");
        }
    }

    private static void EnsureNotSymbolicLinkIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var file = new FileInfo(path);
        if (file.LinkTarget is not null ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "동기화 사진·링크 파일은 심볼릭 링크일 수 없습니다.");
        }
    }

    private static async Task<byte[]> ReadAndValidatePhotoAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (file.Length <= 0 || file.Length > MaximumPhotoBytes)
        {
            throw new InvalidDataException(
                "클라우드 사진 크기가 허용 범위를 벗어났습니다.");
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!string.Equals(
                ComputeSha256(content),
                expectedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "클라우드 사진의 SHA-256이 파일명과 일치하지 않습니다.");
        }

        return content;
    }

    private static async Task WriteAllBytesDurablyAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static string SanitizeFileNamePart(
        string value,
        string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character =>
                invalid.Contains(character) ||
                char.IsWhiteSpace(character)
                    ? '_'
                    : character)
            .ToArray())
            .Trim('.', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        return sanitized.Length <= 80
            ? sanitized
            : sanitized[..80];
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsStoreUnavailable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private SyncStoreUnavailableException CreateUnavailableException(
        Exception exception) =>
        new(
            $"클라우드 동기화 폴더를 사용할 수 없습니다: {_selectedDirectory}",
            exception);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
