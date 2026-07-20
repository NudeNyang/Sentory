using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Sentory.Infrastructure.Updates;

namespace Sentory.Infrastructure.Tests;

public sealed class GitHubReleaseUpdateClientTests
{
    [Fact]
    public void UpdateMetadataDoesNotExposeVerboseReleaseBody()
    {
        Assert.DoesNotContain(
            typeof(ReleaseUpdate).GetProperties(),
            property => property.Name == "Notes");
    }

    [Fact]
    public async Task SelectsArchitectureAndPackageThenVerifiesDownload()
    {
        var payload = "updated sentory"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases"))
            {
                return Json($$"""
                    [{
                      "tag_name":"v0.9.1-beta",
                      "name":"Sentory 0.9.1 beta",
                      "body":"changes",
                      "html_url":"https://github.com/NudeNyang/Sentory/releases/tag/v0.9.1-beta",
                      "draft":false,
                      "prerelease":true,
                      "assets":[{
                        "name":"Sentory-win-x64-portable.zip",
                        "browser_download_url":"https://example.test/update.zip",
                        "digest":"sha256:{{hash}}"
                      }]
                    }]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });
        using var http = new HttpClient(handler);
        using var client = new GitHubReleaseUpdateClient(http);

        var update = await client.CheckAsync(
            "0.9.0-beta",
            Architecture.X64,
            UpdatePackageKind.Portable);
        Assert.NotNull(update);
        Assert.Equal("0.9.1-beta", update.Version);

        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = await client.DownloadAsync(update, directory);
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StableBuildIgnoresPrerelease()
    {
        var handler = new StubHandler(_ => Json("""
            [{
              "tag_name":"v1.1.0-beta",
              "name":"beta","body":"","html_url":"https://example.test/release",
              "draft":false,"prerelease":true,"assets":[]
            }]
            """));
        using var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var update = await client.CheckAsync(
            "1.0.0",
            Architecture.X64,
            UpdatePackageKind.Portable);

        Assert.Null(update);
    }

    [Fact]
    public async Task RejectsDownloadedPackageWithWrongHash()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases"))
            {
                return Json("""
                    [{
                      "tag_name":"v0.9.1-beta","name":"update","body":"",
                      "html_url":"https://example.test/release","draft":false,
                      "prerelease":true,"assets":[{
                        "name":"Sentory-win-x64-portable.zip",
                        "browser_download_url":"https://example.test/update.zip",
                        "digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                      }]
                    }]
                    """);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("tampered"u8.ToArray())
            };
        });
        using var client = new GitHubReleaseUpdateClient(new HttpClient(handler));
        var update = await client.CheckAsync(
            "0.9.0-beta", Architecture.X64, UpdatePackageKind.Portable);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadAsync(update!, directory));
            Assert.Empty(Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory)
                : []);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
