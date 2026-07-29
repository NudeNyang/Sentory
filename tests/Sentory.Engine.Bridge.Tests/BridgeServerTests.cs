using System.Text.Json;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Engine.Bridge.Tests;

public sealed class BridgeServerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-bridge-server-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ServeProcessesSeveralRequestsWithOneRepositoryInstance()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize("https://example.com", out var normalized));
        await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            "https://example.com",
            normalized,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "server-test",
            DateTimeOffset.Now,
            ["test"]));
        var service = new GalleryBridgeService(repository, paths);
        var server = new BridgeServer(service);
        var input = string.Join('\n',
            JsonSerializer.Serialize(new
            {
                id = 1,
                command = "health",
                payload = (object?)null
            }, BridgeServer.JsonOptions),
            JsonSerializer.Serialize(new
            {
                id = 2,
                command = "gallery-revision",
                payload = (object?)null
            }, BridgeServer.JsonOptions));
        using var reader = new StringReader(input);
        using var writer = new StringWriter();

        await server.RunAsync(reader, writer);

        var responses = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, responses.Length);
        using var health = JsonDocument.Parse(responses[0]);
        using var revision = JsonDocument.Parse(responses[1]);
        Assert.True(health.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ready", health.RootElement
            .GetProperty("result")
            .GetProperty("status")
            .GetString());
        Assert.True(revision.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(revision.RootElement
            .GetProperty("result")
            .GetProperty("latestItemId")
            .GetString()));
    }

    [Fact]
    public async Task ServeReturnsRequestErrorWithoutClosingStream()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var server = new BridgeServer(new GalleryBridgeService(repository, paths));
        using var reader = new StringReader(
            "{\"id\":7,\"command\":\"unknown\",\"payload\":null}\n" +
            "{\"id\":8,\"command\":\"health\",\"payload\":null}");
        using var writer = new StringWriter();

        await server.RunAsync(reader, writer);

        var responses = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, responses.Length);
        using var failed = JsonDocument.Parse(responses[0]);
        using var recovered = JsonDocument.Parse(responses[1]);
        Assert.False(failed.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(recovered.RootElement.GetProperty("ok").GetBoolean());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
