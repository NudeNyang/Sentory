using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class WebDavSyncObjectStoreTests
{
    [Theory]
    [InlineData("ftp://nas.example.test/Sentory/")]
    [InlineData("relative-webdav")]
    [InlineData("https://user:secret@nas.example.test/Sentory/")]
    [InlineData("https://nas.example.test/Sentory/?token=secret")]
    public void RejectsUnsupportedEndpoint(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            new WebDavSyncObjectStore(endpoint, null, null));
    }

    [Fact]
    public void NormalizesEndpointAndReadablePhotoKey()
    {
        using var store = new WebDavSyncObjectStore(
            "https://nas.example.test/webdav/Sentory",
            "sentory",
            "secret");

        Assert.Equal(
            "https://nas.example.test/webdav/Sentory/",
            store.Endpoint.AbsoluteUri);
        Assert.Equal(
            $"photos/sha256/{new string('a', 64)}.jpg",
            store.CreateImageObjectKey(new string('a', 64), ".JPEG"));
    }

    [Fact]
    public async Task StoresOperationsInternallyAndPublishesReadableLinks()
    {
        var handler = new InMemoryWebDavHandler();
        using var store = new WebDavSyncObjectStore(
            "https://nas.example.test/webdav/Sentory/",
            null,
            null,
            handler);
        var deviceId = SyncDeviceIdentity.Create();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/shared",
            out var normalized));
        var payload = SyncItemPayload.CreateUrl(
            new SyncUrlContent(
                "https://example.com/shared",
                normalized.Value,
                "example.com"),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "webdav-test",
            DateTimeOffset.Parse("2026-07-30T12:34:56Z"),
            ["url-match"]);
        var operation = SyncOperation.Create(
            deviceId,
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            SyncItemPayloadSerializer.Serialize(payload));
        var bytes = SyncOperationSerializer.Serialize(operation);
        var sha256 = Hash(bytes);
        var key = SyncOperationObjectKey.Create(operation);

        Assert.Equal(
            SyncPutResult.Created,
            await store.PutIfAbsentAsync(key, bytes, sha256));
        Assert.True(handler.ContainsPath(
            $"/webdav/Sentory/.sentory/v2/objects/{key}"));
        Assert.Contains(handler.Paths, path =>
            path.StartsWith(
                "/webdav/Sentory/Links/2026/07/2026-07-30_123456_example.com_",
                StringComparison.Ordinal) &&
            path.EndsWith(".txt", StringComparison.Ordinal));

        var page = await store.ListAsync("devices/", null, 20);
        Assert.Equal(key, Assert.Single(page.Items).Key);
        Assert.Equal(bytes, (await store.TryGetAsync(key))!.Content);

        var deletion = SyncOperation.Create(
            deviceId,
            2,
            Guid.NewGuid(),
            SyncOperationKind.Delete,
            DateTimeOffset.UtcNow,
            SyncItemPayloadSerializer.Serialize(payload));
        var deletionBytes = SyncOperationSerializer.Serialize(deletion);
        await store.PutIfAbsentAsync(
            SyncOperationObjectKey.Create(deletion),
            deletionBytes,
            Hash(deletionBytes));

        Assert.DoesNotContain(handler.Paths, path =>
            path.StartsWith(
                "/webdav/Sentory/Links/",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoresReadablePhotosAtTheNASRoot()
    {
        var handler = new InMemoryWebDavHandler();
        using var store = new WebDavSyncObjectStore(
            "https://nas.example.test/webdav/Sentory/",
            null,
            null,
            handler);
        var bytes = Encoding.UTF8.GetBytes("fake-photo-content");
        var sha256 = Hash(bytes);
        var key = store.CreateImageObjectKey(sha256, ".png");

        await store.PutIfAbsentAsync(key, bytes, sha256);

        Assert.True(handler.ContainsPath(
            $"/webdav/Sentory/Photos/{sha256}.png"));
        Assert.Equal(bytes, (await store.TryGetAsync(key))!.Content);
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class InMemoryWebDavHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories =
        [
            "/webdav/Sentory/"
        ];

        public IReadOnlyCollection<string> Paths => _files.Keys;

        public bool ContainsPath(string path) => _files.ContainsKey(path);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            switch (request.Method.Method)
            {
                case "MKCOL":
                    path = EnsureSlash(path);
                    if (_directories.Contains(path))
                    {
                        return Response(HttpStatusCode.MethodNotAllowed);
                    }
                    _directories.Add(path);
                    return Response(HttpStatusCode.Created);
                case "PUT":
                    if (_files.ContainsKey(path) &&
                        request.Headers.TryGetValues("If-None-Match", out _))
                    {
                        return Response(HttpStatusCode.PreconditionFailed);
                    }
                    _files[path] = await request.Content!.ReadAsByteArrayAsync(
                        cancellationToken);
                    return Response(HttpStatusCode.Created);
                case "GET":
                    return _files.TryGetValue(path, out var content)
                        ? Response(HttpStatusCode.OK, content)
                        : Response(HttpStatusCode.NotFound);
                case "HEAD":
                    return Response(_files.ContainsKey(path)
                        ? HttpStatusCode.OK
                        : HttpStatusCode.NotFound);
                case "DELETE":
                    return Response(_files.Remove(path)
                        ? HttpStatusCode.NoContent
                        : HttpStatusCode.NotFound);
                case "PROPFIND":
                    return PropFind(path);
                default:
                    return Response(HttpStatusCode.MethodNotAllowed);
            }
        }

        private HttpResponseMessage PropFind(string path)
        {
            path = EnsureSlash(path);
            if (!_directories.Contains(path))
            {
                return Response(HttpStatusCode.NotFound);
            }

            XNamespace dav = "DAV:";
            var responses = new List<XElement>
            {
                Entry(dav, path, isCollection: true)
            };
            responses.AddRange(_directories
                .Where(value => value != path && IsDirectChild(path, value))
                .Select(value => Entry(dav, value, isCollection: true)));
            responses.AddRange(_files.Keys
                .Where(value => IsDirectChild(path, value))
                .Select(value => Entry(dav, value, isCollection: false)));
            var xml = new XDocument(
                new XElement(dav + "multistatus", responses));
            return new HttpResponseMessage((HttpStatusCode)207)
            {
                Content = new StringContent(
                    xml.ToString(SaveOptions.DisableFormatting),
                    Encoding.UTF8,
                    "application/xml")
            };
        }

        private static XElement Entry(
            XNamespace dav,
            string path,
            bool isCollection) =>
            new(
                dav + "response",
                new XElement(dav + "href", path),
                new XElement(
                    dav + "propstat",
                    new XElement(
                        dav + "prop",
                        new XElement(
                            dav + "resourcetype",
                            isCollection
                                ? new XElement(dav + "collection")
                                : null)),
                    new XElement(dav + "status", "HTTP/1.1 200 OK")));

        private static bool IsDirectChild(string parent, string value)
        {
            if (!value.StartsWith(parent, StringComparison.Ordinal))
            {
                return false;
            }
            var suffix = value[parent.Length..].TrimEnd('/');
            return suffix.Length > 0 && !suffix.Contains('/');
        }

        private static string EnsureSlash(string value) =>
            value.EndsWith('/') ? value : string.Concat(value, "/");

        private static HttpResponseMessage Response(
            HttpStatusCode statusCode,
            byte[]? content = null) =>
            new(statusCode)
            {
                Content = content is null ? null : new ByteArrayContent(content)
            };
    }
}
