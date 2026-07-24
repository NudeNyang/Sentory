using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.App.Tests;

public sealed class CopyUsageRecorderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.CopyUsage.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RepeatedDetailCopiesUpdateCountAndAddFavorite()
    {
        var repository = new SqliteCaptureRepository(
            SentoryDataPaths.ForRoot(_root));
        await repository.InitializeAsync();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var created = await repository.UpsertImageAsync(
            new ImageCaptureRequest(
                Guid.NewGuid(),
                bytes,
                Convert.ToHexString(SHA256.HashData(bytes)),
                1,
                1,
                "image/png",
                ".png",
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedImage,
                DeliveryStatus.Confirmed,
                "detail-copy",
                DateTimeOffset.UtcNow,
                []));
        var item = Assert.Single(await repository.GetRecentAsync(10));
        var recorder = new CopyUsageRecorder(repository);

        var first = await recorder.RecordAsync(
            item,
            autoFavoriteEnabled: true,
            autoFavoriteCopyThreshold: 2,
            DateTimeOffset.UtcNow);
        var second = await recorder.RecordAsync(
            first.Item,
            autoFavoriteEnabled: true,
            autoFavoriteCopyThreshold: 2,
            DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(created.ItemId, second.Item.ItemId);
        Assert.Equal(2, second.Item.CopyCount);
        Assert.True(second.Item.IsFavorite);
        Assert.Equal(
            CopyUsageRecordOutcome.AutoFavoriteAdded,
            second.Outcome);

        var persisted = Assert.Single(
            await repository.GetRecentAsync(10));
        Assert.Equal(2, persisted.CopyCount);
        Assert.True(persisted.IsFavorite);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
