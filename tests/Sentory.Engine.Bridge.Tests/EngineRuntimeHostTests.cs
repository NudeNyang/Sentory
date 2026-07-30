using System.Text.Json;
using Sentory.Infrastructure.Data;

namespace Sentory.Engine.Bridge.Tests;

public sealed class EngineRuntimeHostTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-engine-runtime-host-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LinkPreviewBatchPublishesGalleryChangedEvent()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var attempts = 0;
        await using var host = new EngineRuntimeHost(
            repository,
            paths,
            (limit, retryBefore, cancellationToken) =>
            {
                Assert.Equal(4, limit);
                Assert.True(retryBefore <= DateTimeOffset.UtcNow.AddDays(-29));
                Assert.False(cancellationToken.IsCancellationRequested);
                attempts++;
                return Task.FromResult(2);
            });

        var updated = await host.EnrichLinkPreviewsOnceAsync();
        var poll = host.Poll();

        Assert.Equal(2, updated);
        Assert.Equal(1, attempts);
        var runtimeEvent = Assert.Single(poll.Events);
        Assert.Equal("gallery-changed", runtimeEvent.Type);
        var payload = JsonSerializer.SerializeToElement(
            runtimeEvent.Payload,
            BridgeServer.JsonOptions);
        Assert.Equal("link-preview", payload.GetProperty("reason").GetString());
        Assert.Equal(2, payload.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task LinkPreviewBatchDoesNotPublishEventWithoutChanges()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        await using var host = new EngineRuntimeHost(
            repository,
            paths,
            (_, _, _) => Task.FromResult(0));

        var updated = await host.EnrichLinkPreviewsOnceAsync();
        var poll = host.Poll();

        Assert.Equal(0, updated);
        Assert.Empty(poll.Events);
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
