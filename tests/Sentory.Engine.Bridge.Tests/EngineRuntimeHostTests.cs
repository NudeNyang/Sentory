using System.Text.Json;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Ocr;

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

    [Fact]
    public async Task ImageOcrBatchPublishesGalleryChangedEvent()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var attempts = 0;
        await using var host = new EngineRuntimeHost(
            repository,
            paths,
            (_, _, _) => Task.FromResult(0),
            (limit, cancellationToken) =>
            {
                Assert.Equal(1, limit);
                Assert.False(cancellationToken.IsCancellationRequested);
                attempts++;
                return Task.FromResult(new OcrEnrichmentBatchResult(1, 1));
            });

        var result = await host.EnrichOcrOnceAsync();
        var poll = host.Poll();

        Assert.Equal(new OcrEnrichmentBatchResult(1, 1), result);
        Assert.Equal(1, attempts);
        var runtimeEvent = Assert.Single(poll.Events);
        Assert.Equal("gallery-changed", runtimeEvent.Type);
        var payload = JsonSerializer.SerializeToElement(
            runtimeEvent.Payload,
            BridgeServer.JsonOptions);
        Assert.Equal("image-ocr", payload.GetProperty("reason").GetString());
        Assert.Equal(1, payload.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task ConfiguredFolderUsesTheSharedSyncRuntime()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var folder = Path.Combine(_root, "cloud", "Sentory");
        await using var host = new EngineRuntimeHost(repository, paths);

        var configured = await host.ConfigureSyncFolderAsync(folder);
        await host.RunConfiguredSyncOnceAsync();
        var completed = host.GetSettings();

        Assert.True(configured.Sync.Enabled);
        Assert.Equal("Folder", configured.Sync.Provider);
        Assert.Equal(Path.GetFullPath(folder), configured.Sync.FolderPath);
        Assert.Equal("Succeeded", completed.Sync.State);
        Assert.True(Directory.Exists(Path.Combine(folder, ".sentory", "v2")));
    }

    [Fact]
    public void WebDavCredentialIsEncryptedForTheCurrentWindowsUser()
    {
        var encrypted = WebDavCredentialProtector.Protect("nas-password");

        Assert.NotEqual("nas-password", encrypted);
        Assert.Equal(
            "nas-password",
            WebDavCredentialProtector.Unprotect(encrypted));
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
