using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SyncFolderCapabilityProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.SyncFolderCapability.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritableFolderPassesCapabilityProbeWithoutResidue()
    {
        Directory.CreateDirectory(_root);

        var result = await SyncFolderCapabilityProbe.CheckAsync(_root);

        Assert.True(result.IsSupported);
        Assert.Null(result.FailureReason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task FilePathFailsCapabilityProbeWithSpecificReason()
    {
        Directory.CreateDirectory(_root);
        var filePath = Path.Combine(_root, "not-a-folder");
        await File.WriteAllTextAsync(filePath, "content");

        var result = await SyncFolderCapabilityProbe.CheckAsync(filePath);

        Assert.False(result.IsSupported);
        Assert.Equal(
            SyncFolderCapabilityFailure.NotDirectory,
            result.FailureReason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
