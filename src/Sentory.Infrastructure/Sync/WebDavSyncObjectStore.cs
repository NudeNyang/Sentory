using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Sentory.Core;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Sync;

public sealed class WebDavSyncObjectStore : IReadableSyncObjectStore, IDisposable
{
    private const string InternalObjectsPrefix = ".sentory/v2/objects/";
    private const string TemporaryContentPrefix =
        ".sentory/v2/temporary-content/";
    private const long MaximumObjectBytes = 100L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public WebDavSyncObjectStore(
        string endpoint,
        string? username,
        string? password,
        HttpMessageHandler? handler = null)
    {
        Endpoint = NormalizeEndpoint(endpoint);
        if (handler is null)
        {
            var httpHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                PreAuthenticate = !string.IsNullOrWhiteSpace(username)
            };
            if (!string.IsNullOrWhiteSpace(username))
            {
                httpHandler.Credentials = new NetworkCredential(
                    username,
                    password ?? string.Empty);
            }

            handler = httpHandler;
        }

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _ownsHttpClient = true;
    }

    public Uri Endpoint { get; }

    public string CreateImageObjectKey(
        string sha256,
        string fileExtension) =>
        SyncBlobObjectKey.CreateReadable(sha256, fileExtension);

    public async Task ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var key = string.Concat(
            TemporaryContentPrefix,
            Guid.NewGuid().ToString("N"),
            ".probe");
        var content = RandomNumberGenerator.GetBytes(32);
        var sha256 = ComputeSha256(content);
        try
        {
            await PutRawIfAbsentAsync(
                key,
                content,
                sha256,
                cancellationToken);
            var stored = await TryGetRawAsync(key, cancellationToken) ??
                         throw new SyncStoreUnavailableException(
                             "NAS에 쓴 연결 확인 파일을 다시 읽지 못했습니다.");
            if (!stored.Content.AsSpan().SequenceEqual(content))
            {
                throw new SyncStoreUnavailableException(
                    "NAS에 쓴 연결 확인 파일의 내용이 달라졌습니다.");
            }
        }
        finally
        {
            await DeleteRawIfPresentAsync(key, cancellationToken);
        }
    }

    public async Task<SyncObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var logicalPrefix = ValidateKey(prefix, allowTrailingSlash: true);
        var storagePrefix = ToStorageKey(logicalPrefix);
        var files = await EnumerateFilesAsync(
            storagePrefix,
            cancellationToken);
        var logicalKeys = files
            .Select(ToLogicalKey)
            .Where(key => key.StartsWith(logicalPrefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var offset = 0;
        if (continuationToken is not null &&
            (!int.TryParse(
                 continuationToken,
                 NumberStyles.None,
                 CultureInfo.InvariantCulture,
                 out offset) ||
             offset < 0 ||
             offset > logicalKeys.Length))
        {
            throw new InvalidDataException(
                "NAS 동기화 목록의 이어받기 토큰이 올바르지 않습니다.");
        }

        var selected = logicalKeys.Skip(offset).Take(pageSize).ToArray();
        var items = new List<SyncObjectInfo>(selected.Length);
        foreach (var key in selected)
        {
            var stored = await TryGetAsync(key, cancellationToken) ??
                         throw new InvalidDataException(
                             $"NAS 목록에 있던 파일을 읽지 못했습니다: {key}");
            items.Add(new SyncObjectInfo(
                key,
                stored.Content.LongLength,
                stored.Sha256));
        }

        var nextOffset = offset + selected.Length;
        return new SyncObjectPage(
            items,
            nextOffset < logicalKeys.Length
                ? nextOffset.ToString(CultureInfo.InvariantCulture)
                : null);
    }

    public async Task<SyncPutResult> PutIfAbsentAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        var logicalKey = ValidateKey(key);
        ValidateContent(content, sha256);
        if (SyncOperationObjectKey.TryParse(
                logicalKey,
                out _,
                out _,
                out _))
        {
            await ApplyReadableOperationAsync(
                content,
                cancellationToken);
        }

        return await PutRawIfAbsentAsync(
            ToStorageKey(logicalKey),
            content,
            sha256,
            cancellationToken);
    }

    public async Task<SyncStoredObject?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var logicalKey = ValidateKey(key);
        var stored = await TryGetRawAsync(
            ToStorageKey(logicalKey),
            cancellationToken);
        return stored is null
            ? null
            : stored with { Key = logicalKey };
    }

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var logicalKey = ValidateKey(key);
        return await ExistsRawAsync(
            ToStorageKey(logicalKey),
            cancellationToken);
    }

    private async Task ApplyReadableOperationAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        SyncOperation operation;
        SyncItemPayload payload;
        try
        {
            operation = SyncOperationSerializer.Deserialize(content.Span);
            payload = SyncItemPayloadSerializer.Deserialize(operation.Payload);
        }
        catch (Exception exception)
            when (exception is InvalidDataException or NotSupportedException)
        {
            return;
        }

        if (operation.Kind == SyncOperationKind.Delete)
        {
            await DeleteReadableContentAsync(payload, cancellationToken);
            return;
        }

        if (operation.Kind != SyncOperationKind.Upsert ||
            payload.Url is not { } url)
        {
            return;
        }

        var capturedAt = payload.CapturedAt;
        var domain = SanitizePathPart(url.Domain, "link");
        var key = string.Join(
            '/',
            "Links",
            capturedAt.Year.ToString("D4", CultureInfo.InvariantCulture),
            capturedAt.Month.ToString("D2", CultureInfo.InvariantCulture),
            string.Concat(
                capturedAt.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture),
                "_",
                domain,
                "_",
                operation.OperationId.ToString("N"),
                ".txt"));
        var text = string.Join(
            Environment.NewLine,
            $"주소: {url.OriginalUrl}",
            $"도메인: {url.Domain}",
            $"저장 시각: {capturedAt:O}",
            $"출처: {payload.SourceApp}",
            string.Empty);
        var bytes = new UTF8Encoding(false).GetBytes(text);
        await PutRawIfAbsentAsync(
            key,
            bytes,
            ComputeSha256(bytes),
            cancellationToken);
    }

    private async Task DeleteReadableContentAsync(
        SyncItemPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Image is { } image)
        {
            await DeleteRawIfPresentAsync(
                ToStorageKey(SyncBlobObjectKey.CreateReadable(
                    image.ContentSha256.ToLowerInvariant(),
                    image.FileExtension)),
                cancellationToken);
            return;
        }

        if (payload.Url is not { } url)
        {
            return;
        }

        foreach (var key in await EnumerateFilesAsync(
                     "Links/",
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stored = await TryGetRawAsync(key, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            using var reader = new StringReader(
                Encoding.UTF8.GetString(stored.Content));
            var firstLine = reader.ReadLine();
            const string addressPrefix = "주소: ";
            if (firstLine is null ||
                !firstLine.StartsWith(addressPrefix, StringComparison.Ordinal) ||
                !UrlNormalizer.TryNormalize(
                    firstLine[addressPrefix.Length..],
                    out var normalized) ||
                !string.Equals(
                    normalized.Value,
                    url.NormalizedUrl,
                    StringComparison.Ordinal))
            {
                continue;
            }

            await DeleteRawIfPresentAsync(key, cancellationToken);
        }
    }

    private async Task<SyncPutResult> PutRawIfAbsentAsync(
        string storageKey,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken)
    {
        ValidateContent(content, sha256);
        var existing = await TryGetRawAsync(storageKey, cancellationToken);
        if (existing is not null)
        {
            ValidateExisting(storageKey, existing, content, sha256);
            return SyncPutResult.AlreadyExists;
        }

        await EnsureParentCollectionsAsync(storageKey, cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                CreateUri(storageKey));
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            request.Content = new ByteArrayContent(content.ToArray());
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/octet-stream");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed ||
                response.StatusCode == HttpStatusCode.Conflict)
            {
                existing = await TryGetRawAsync(
                    storageKey,
                    cancellationToken) ??
                           throw CreateUnavailable(
                               $"NAS 파일 충돌을 확인하지 못했습니다: {storageKey}");
                ValidateExisting(storageKey, existing, content, sha256);
                return SyncPutResult.AlreadyExists;
            }

            EnsureSuccess(response, "NAS에 동기화 파일을 쓰지 못했습니다.");
            return SyncPutResult.Created;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            throw CreateUnavailable(
                $"NAS에 동기화 파일을 쓰지 못했습니다: {storageKey}",
                exception);
        }
    }

    private async Task<SyncStoredObject?> TryGetRawAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                CreateUri(storageKey),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            EnsureSuccess(response, "NAS 동기화 파일을 읽지 못했습니다.");
            if (response.Content.Headers.ContentLength is > MaximumObjectBytes)
            {
                throw new InvalidDataException(
                    "NAS 동기화 파일 크기가 허용 범위를 넘었습니다.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var buffer = new MemoryStream();
            await CopyToLimitedAsync(stream, buffer, cancellationToken);
            var content = buffer.ToArray();
            return new SyncStoredObject(
                storageKey,
                ComputeSha256(content),
                content);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            throw CreateUnavailable(
                $"NAS 동기화 파일을 읽지 못했습니다: {storageKey}",
                exception);
        }
    }

    private async Task<bool> ExistsRawAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Head,
                CreateUri(storageKey));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            EnsureSuccess(response, "NAS 동기화 파일 상태를 확인하지 못했습니다.");
            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            throw CreateUnavailable(
                $"NAS 동기화 파일 상태를 확인하지 못했습니다: {storageKey}",
                exception);
        }
    }

    private async Task<IReadOnlyList<string>> EnumerateFilesAsync(
        string storagePrefix,
        CancellationToken cancellationToken)
    {
        var prefix = ValidateKey(storagePrefix, allowTrailingSlash: true);
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(prefix);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collection = pending.Dequeue();
            if (!visited.Add(collection))
            {
                continue;
            }

            foreach (var entry in await PropFindAsync(
                         collection,
                         cancellationToken))
            {
                if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.IsCollection)
                {
                    pending.Enqueue(EnsureTrailingSlash(entry.Key));
                }
                else
                {
                    files.Add(entry.Key);
                }
            }
        }

        return files.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyList<WebDavEntry>> PropFindAsync(
        string collectionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                new HttpMethod("PROPFIND"),
                CreateUri(collectionKey));
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>",
                Encoding.UTF8,
                "application/xml");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            EnsureSuccess(response, "NAS 폴더 목록을 읽지 못했습니다.");
            var xml = await response.Content.ReadAsStringAsync(
                cancellationToken);
            var document = XDocument.Parse(xml, LoadOptions.None);
            var current = EnsureTrailingSlash(collectionKey);
            var entries = new List<WebDavEntry>();
            foreach (var element in document.Descendants()
                         .Where(value => value.Name.LocalName == "response"))
            {
                var href = element.Descendants()
                    .FirstOrDefault(value => value.Name.LocalName == "href")
                    ?.Value;
                if (!TryGetStorageKey(href, out var key) ||
                    string.Equals(
                        EnsureTrailingSlash(key),
                        current,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var isCollection = element.Descendants()
                    .Any(value => value.Name.LocalName == "collection");
                entries.Add(new WebDavEntry(
                    isCollection ? EnsureTrailingSlash(key) : key,
                    isCollection));
            }

            return entries;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            throw CreateUnavailable(
                $"NAS 폴더 목록을 읽지 못했습니다: {collectionKey}",
                exception);
        }
    }

    private async Task EnsureParentCollectionsAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        await EnsureCollectionAsync(string.Empty, cancellationToken);
        var parts = storageKey.Split('/');
        var current = string.Empty;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            current = string.Concat(current, parts[index], "/");
            await EnsureCollectionAsync(current, cancellationToken);
        }
    }

    private async Task EnsureCollectionAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                new HttpMethod("MKCOL"),
                CreateUri(storageKey));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode ||
                response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                return;
            }

            EnsureSuccess(response, "NAS 동기화 폴더를 만들지 못했습니다.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            throw CreateUnavailable(
                $"NAS 동기화 폴더를 만들지 못했습니다: {storageKey}",
                exception);
        }
    }

    private async Task DeleteRawIfPresentAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                CreateUri(storageKey));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound ||
                response.IsSuccessStatusCode)
            {
                return;
            }

            EnsureSuccess(response, "NAS 동기화 파일을 지우지 못했습니다.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            throw CreateUnavailable(
                $"NAS 동기화 파일을 지우지 못했습니다: {storageKey}",
                exception);
        }
    }

    private static Uri NormalizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "NAS WebDAV 주소는 http 또는 https 절대 주소여야 합니다.",
                nameof(endpoint));
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsolutePath
                : string.Concat(uri.AbsolutePath, "/")
        };
        return builder.Uri;
    }

    private Uri CreateUri(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
        {
            return Endpoint;
        }

        var escaped = string.Join(
            '/',
            storageKey.Split('/').Select(Uri.EscapeDataString));
        return new Uri(Endpoint, escaped);
    }

    private bool TryGetStorageKey(string? href, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(href) ||
            !Uri.TryCreate(Endpoint, href, out var uri) ||
            !string.Equals(uri.Scheme, Endpoint.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != Endpoint.Port)
        {
            return false;
        }

        var relative = Uri.UnescapeDataString(
            Endpoint.MakeRelativeUri(uri).ToString())
            .Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) ||
            relative.Contains("/../", StringComparison.Ordinal))
        {
            return false;
        }

        key = relative.TrimStart('/');
        return true;
    }

    private static string ToStorageKey(string logicalKey)
    {
        if (logicalKey.StartsWith(
                SyncOperationObjectKey.OperationsPrefix,
                StringComparison.Ordinal))
        {
            return string.Concat(InternalObjectsPrefix, logicalKey);
        }

        return SyncBlobObjectKey.TryParseReadable(
            logicalKey,
            out var sha256,
            out var extension)
            ? string.Concat("Photos/", sha256, extension)
            : logicalKey;
    }

    private static string ToLogicalKey(string storageKey)
    {
        if (storageKey.StartsWith(
                string.Concat(
                    InternalObjectsPrefix,
                    SyncOperationObjectKey.OperationsPrefix),
                StringComparison.Ordinal))
        {
            return storageKey[InternalObjectsPrefix.Length..];
        }

        if (storageKey.StartsWith("Photos/", StringComparison.Ordinal))
        {
            var fileName = storageKey["Photos/".Length..];
            if (!fileName.Contains('/') && fileName.Length > 64)
            {
                var sha256 = fileName[..64];
                var extension = fileName[64..];
                try
                {
                    return SyncBlobObjectKey.CreateReadable(
                        sha256,
                        extension);
                }
                catch (Exception exception)
                    when (exception is ArgumentException or NotSupportedException)
                {
                }
            }
        }

        return storageKey;
    }

    private static string ValidateKey(
        string key,
        bool allowTrailingSlash = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = key.Replace('\\', '/').TrimStart('/');
        if (!allowTrailingSlash)
        {
            normalized = normalized.TrimEnd('/');
        }

        var parts = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 ||
            parts.Any(part => part is "." or ".." || part.Contains('\0')))
        {
            throw new ArgumentException(
                "NAS 동기화 파일 경로가 올바르지 않습니다.",
                nameof(key));
        }

        return string.Join('/', parts) +
               (allowTrailingSlash ? "/" : string.Empty);
    }

    private static void ValidateContent(
        ReadOnlyMemory<byte> content,
        string sha256)
    {
        if (content.Length <= 0 || content.Length > MaximumObjectBytes ||
            !string.Equals(
                ComputeSha256(content.Span),
                sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "NAS에 저장할 파일 내용과 SHA-256이 일치하지 않습니다.");
        }
    }

    private static void ValidateExisting(
        string key,
        SyncStoredObject existing,
        ReadOnlyMemory<byte> expected,
        string sha256)
    {
        if (!string.Equals(existing.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ||
            !existing.Content.AsSpan().SequenceEqual(expected.Span))
        {
            throw new InvalidDataException(
                $"NAS의 같은 동기화 키에 다른 내용이 있습니다: {key}");
        }
    }

    private static async Task CopyToLimitedAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > MaximumObjectBytes)
            {
                throw new InvalidDataException(
                    "NAS 동기화 파일 크기가 허용 범위를 넘었습니다.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    private static string SanitizePathPart(string value, string fallback)
    {
        var sanitized = new string(value
            .Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '_')
            .ToArray())
            .Trim('.', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal)
            ? value
            : string.Concat(value, "/");

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsNetworkFailure(Exception exception) =>
        exception is HttpRequestException or IOException or
            UnauthorizedAccessException or System.Xml.XmlException;

    private void EnsureSuccess(HttpResponseMessage response, string message)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "NAS 계정이나 권한을 확인해 주세요.",
            HttpStatusCode.NotFound =>
                "NAS WebDAV 경로를 찾지 못했습니다.",
            _ => $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
        };
        throw CreateUnavailable($"{message} {detail}");
    }

    private SyncStoreUnavailableException CreateUnavailable(
        string message,
        Exception? exception = null) =>
        new($"{message} ({Endpoint})", exception);

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record WebDavEntry(string Key, bool IsCollection);
}
