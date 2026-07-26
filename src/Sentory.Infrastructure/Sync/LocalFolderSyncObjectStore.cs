using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Sync;

public sealed class LocalFolderSyncObjectStore : ISyncObjectStore
{
    public const long MaximumObjectBytes = 128L * 1024 * 1024;
    private const int MaximumKeyLength = 1024;
    private const int MaximumPageSize = 1000;
    private const string ObjectFileSuffix = ".sobj";
    private const int HeaderSize = 8 + 1 + 8 + 32;
    private static readonly byte[] Magic = "SENTORY1"u8.ToArray();
    private readonly string _selectedDirectory;
    private readonly string _objectsDirectory;
    private readonly string _temporaryDirectory;

    public LocalFolderSyncObjectStore(string selectedDirectory)
        : this(
            selectedDirectory,
            Path.Combine("Sentory Sync", "v1"))
    {
    }

    internal LocalFolderSyncObjectStore(
        string selectedDirectory,
        string storeRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRelativePath);
        var selectedRoot = Path.GetFullPath(selectedDirectory);
        if (Path.IsPathRooted(storeRelativePath) ||
            storeRelativePath.Split(
                    [Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                "동기화 저장소 상대 경로가 올바르지 않습니다.",
                nameof(storeRelativePath));
        }

        _selectedDirectory = selectedRoot;
        StoreDirectory = Path.Combine(
            selectedRoot,
            storeRelativePath);
        _objectsDirectory = Path.Combine(StoreDirectory, "objects");
        _temporaryDirectory = Path.Combine(StoreDirectory, "temporary");
    }

    public string StoreDirectory { get; }

    public async Task<SyncObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidatePrefix(prefix);
        if (pageSize <= 0 || pageSize > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var afterKey = DecodeContinuationToken(continuationToken);
        try
        {
            EnsureStoreDirectories();
            var values = new List<SyncObjectInfo>();
            foreach (var path in EnumerateObjectFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = TryGetKey(path);
                if (key is null ||
                    !key.StartsWith(prefix, StringComparison.Ordinal) ||
                    (afterKey is not null &&
                     string.CompareOrdinal(key, afterKey) <= 0))
                {
                    continue;
                }

                var header = await TryReadCompleteHeaderAsync(
                    path,
                    cancellationToken);
                if (header is not null)
                {
                    values.Add(new SyncObjectInfo(
                        key,
                        header.ContentLength,
                        header.Sha256));
                }
            }

            var ordered = values
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Take(pageSize + 1)
                .ToArray();
            var page = ordered.Take(pageSize).ToArray();
            var nextToken = ordered.Length > pageSize
                ? EncodeContinuationToken(page[^1].Key)
                : null;
            return new SyncObjectPage(page, nextToken);
        }
        catch (Exception exception) when (IsStoreUnavailable(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<SyncPutResult> PutIfAbsentAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateContentLength(content.Length);
        if (!SyncHash.IsSha256(sha256))
        {
            throw new ArgumentException(
                "동기화 객체 SHA-256 형식이 올바르지 않습니다.",
                nameof(sha256));
        }

        var normalizedSha256 = sha256.ToLowerInvariant();
        var actualSha256 = ComputeSha256(content.Span);
        if (!string.Equals(
                actualSha256,
                normalizedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "동기화 객체 내용과 SHA-256이 일치하지 않습니다.");
        }

        try
        {
            EnsureStoreDirectories();
            var targetPath = GetObjectPath(key);
            EnsureSafeParentDirectories(targetPath);
            if (File.Exists(targetPath))
            {
                return await ValidateExistingAsync(
                    targetPath,
                    key,
                    content.Length,
                    normalizedSha256,
                    cancellationToken);
            }

            var temporaryPath = Path.Combine(
                _temporaryDirectory,
                $"{Guid.NewGuid():N}.partial");
            try
            {
                await WriteObjectAsync(
                    temporaryPath,
                    content,
                    normalizedSha256,
                    cancellationToken);
                try
                {
                    File.Move(
                        temporaryPath,
                        targetPath,
                        overwrite: false);
                    return SyncPutResult.Created;
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    return await ValidateExistingAsync(
                        targetPath,
                        key,
                        content.Length,
                        normalizedSha256,
                        cancellationToken);
                }
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
        catch (Exception exception) when (IsStoreUnavailable(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<SyncStoredObject?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        try
        {
            EnsureStoreDirectories();
            var path = GetObjectPath(key);
            if (!File.Exists(path))
            {
                return null;
            }

            EnsureNotSymbolicLink(path);
            var value = await ReadObjectAsync(
                path,
                cancellationToken);
            if (value is null)
            {
                throw new SyncStoreUnavailableException(
                    "클라우드 동기화 객체가 아직 복사 중입니다.");
            }

            return new SyncStoredObject(
                key,
                value.Sha256,
                value.Content);
        }
        catch (Exception exception) when (IsStoreUnavailable(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        try
        {
            EnsureStoreDirectories();
            var path = GetObjectPath(key);
            if (!File.Exists(path))
            {
                return false;
            }

            EnsureNotSymbolicLink(path);
            return await TryReadCompleteHeaderAsync(
                path,
                cancellationToken) is not null;
        }
        catch (Exception exception) when (IsStoreUnavailable(exception))
        {
            throw CreateUnavailableException(exception);
        }
    }

    internal string GetObjectPathForTesting(string key)
    {
        ValidateKey(key);
        return GetObjectPath(key);
    }

    private IEnumerable<string> EnumerateObjectFiles()
    {
        var pending = new Stack<string>();
        pending.Push(_objectsDirectory);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!IsSymbolicLink(new DirectoryInfo(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                if (entry.EndsWith(
                        ObjectFileSuffix,
                        StringComparison.Ordinal) &&
                    !IsSymbolicLink(new FileInfo(entry)))
                {
                    yield return entry;
                }
            }
        }
    }

    private string? TryGetKey(string path)
    {
        var relative = Path.GetRelativePath(
            _objectsDirectory,
            path);
        if (relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            !relative.EndsWith(
                ObjectFileSuffix,
                StringComparison.Ordinal))
        {
            return null;
        }

        var withoutSuffix = relative[..^ObjectFileSuffix.Length];
        var key = withoutSuffix.Replace(
            Path.DirectorySeparatorChar,
            '/');
        if (Path.AltDirectorySeparatorChar !=
            Path.DirectorySeparatorChar)
        {
            key = key.Replace(
                Path.AltDirectorySeparatorChar,
                '/');
        }

        try
        {
            ValidateKey(key);
            return key;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string GetObjectPath(string key)
    {
        var segments = key.Split('/');
        var path = _objectsDirectory;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return string.Concat(path, ObjectFileSuffix);
    }

    private void EnsureStoreDirectories()
    {
        Directory.CreateDirectory(_selectedDirectory);
        var relativeStore = Path.GetRelativePath(
            _selectedDirectory,
            StoreDirectory);
        var current = _selectedDirectory;
        foreach (var segment in relativeStore.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            EnsureManagedDirectory(current);
        }

        EnsureManagedDirectory(_objectsDirectory);
        EnsureManagedDirectory(_temporaryDirectory);
    }

    private void EnsureSafeParentDirectories(string targetPath)
    {
        var relative = Path.GetRelativePath(
            _objectsDirectory,
            Path.GetDirectoryName(targetPath)!);
        var current = _objectsDirectory;
        if (relative == ".")
        {
            return;
        }

        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            EnsureNotSymbolicLink(current);
        }

        if (File.Exists(targetPath))
        {
            EnsureNotSymbolicLink(targetPath);
        }
    }

    private static void EnsureManagedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        EnsureNotSymbolicLink(path);
    }

    private static async Task WriteObjectAsync(
        string path,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken)
    {
        var header = CreateHeader(content.Length, sha256);
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task<SyncPutResult> ValidateExistingAsync(
        string path,
        string key,
        int expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        EnsureNotSymbolicLink(path);
        var existing = await ReadObjectAsync(path, cancellationToken);
        if (existing is null)
        {
            throw new SyncStoreUnavailableException(
                "같은 키의 클라우드 객체가 아직 복사 중입니다.");
        }

        if (existing.Content.LongLength != expectedLength ||
            !string.Equals(
                existing.Sha256,
                expectedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"같은 동기화 키에 다른 내용이 있습니다: {key}");
        }

        return SyncPutResult.AlreadyExists;
    }

    private static async Task<StoredValue?> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        var header = await TryReadHeaderAsync(
            stream,
            cancellationToken);
        if (header is null ||
            stream.Length != HeaderSize + header.ContentLength)
        {
            return null;
        }

        var content = new byte[checked((int)header.ContentLength)];
        if (!await ReadExactlyAsync(
                stream,
                content,
                cancellationToken))
        {
            return null;
        }

        var actualSha256 = ComputeSha256(content);
        if (!string.Equals(
                actualSha256,
                header.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "클라우드 동기화 객체의 SHA-256이 일치하지 않습니다.");
        }

        return new StoredValue(header.Sha256, content);
    }

    private static async Task<ObjectHeader?> TryReadCompleteHeaderAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        var header = await TryReadHeaderAsync(
            stream,
            cancellationToken);
        return header is not null &&
               stream.Length == HeaderSize + header.ContentLength
            ? header
            : null;
    }

    private static async Task<ObjectHeader?> TryReadHeaderAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        if (stream.Length < HeaderSize)
        {
            return null;
        }

        var header = new byte[HeaderSize];
        if (!await ReadExactlyAsync(
                stream,
                header,
                cancellationToken) ||
            !header.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            header[Magic.Length] != 1)
        {
            return null;
        }

        var contentLength = BinaryPrimitives.ReadInt64LittleEndian(
            header.AsSpan(Magic.Length + 1, sizeof(long)));
        if (contentLength < 0 ||
            contentLength > MaximumObjectBytes)
        {
            return null;
        }

        var sha256 = Convert.ToHexString(
            header.AsSpan(Magic.Length + 1 + sizeof(long), 32))
            .ToLowerInvariant();
        return new ObjectHeader(contentLength, sha256);
    }

    private static async Task<bool> ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer[totalRead..],
                cancellationToken);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static FileStream OpenRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);

    private static byte[] CreateHeader(
        int contentLength,
        string sha256)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[Magic.Length] = 1;
        BinaryPrimitives.WriteInt64LittleEndian(
            header.AsSpan(Magic.Length + 1, sizeof(long)),
            contentLength);
        Convert.FromHexString(sha256).CopyTo(
            header,
            Magic.Length + 1 + sizeof(long));
        return header;
    }

    private static void ValidateContentLength(int contentLength)
    {
        if (contentLength < 0 ||
            contentLength > MaximumObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentLength));
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaximumKeyLength ||
            key.StartsWith('/') ||
            key.EndsWith('/') ||
            key.Contains('\\'))
        {
            throw new ArgumentException(
                "동기화 객체 키 형식이 올바르지 않습니다.",
                nameof(key));
        }

        var segments = key.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".." ||
                segment.Length > 200 ||
                segment.Any(character =>
                    character is not (>= 'a' and <= 'z') &&
                    character is not (>= '0' and <= '9') &&
                    character is not '-' and not '_' and not '.')))
        {
            throw new ArgumentException(
                "동기화 객체 키 형식이 올바르지 않습니다.",
                nameof(key));
        }
    }

    private static void ValidatePrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length == 0)
        {
            return;
        }

        var candidate = prefix.EndsWith('/')
            ? prefix[..^1]
            : prefix;
        ValidateKey(candidate);
    }

    private static string? DecodeContinuationToken(string? token)
    {
        if (token is null)
        {
            return null;
        }

        try
        {
            var padded = token
                .Replace('-', '+')
                .Replace('_', '/');
            padded = padded.PadRight(
                padded.Length + ((4 - padded.Length % 4) % 4),
                '=');
            var key = Encoding.UTF8.GetString(
                Convert.FromBase64String(padded));
            ValidateKey(key);
            return key;
        }
        catch (Exception exception)
            when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException(
                "동기화 목록 이어받기 토큰이 올바르지 않습니다.",
                nameof(token),
                exception);
        }
    }

    private static string EncodeContinuationToken(string key) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsSymbolicLink(FileSystemInfo info)
    {
        info.Refresh();
        return info.Exists && info.LinkTarget is not null;
    }

    private static void EnsureNotSymbolicLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        if (IsSymbolicLink(info))
        {
            throw new InvalidDataException(
                "동기화 저장소 안의 심볼릭 링크는 사용할 수 없습니다.");
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsStoreUnavailable(Exception exception) =>
        exception is IOException and
            not SyncStoreUnavailableException ||
        exception is UnauthorizedAccessException;

    private static SyncStoreUnavailableException
        CreateUnavailableException(Exception exception) =>
        exception as SyncStoreUnavailableException ??
        new SyncStoreUnavailableException(
            "로컬 클라우드 동기화 폴더를 사용할 수 없습니다.",
            exception);

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ObjectHeader(
        long ContentLength,
        string Sha256);

    private sealed record StoredValue(
        string Sha256,
        byte[] Content);
}
