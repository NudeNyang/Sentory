using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WindowsCloudSyncFolderDiscoveryTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "Sentory.CloudSyncFolderDiscovery.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveCreatesOneSentoryFolderPerAvailableProvider()
    {
        var oneDrive = Directory.CreateDirectory(
            Path.Combine(_testRoot, "OneDrive")).FullName;
        var googleDrive = Directory.CreateDirectory(
            Path.Combine(_testRoot, "Google Drive", "My Drive")).FullName;
        var dropbox = Directory.CreateDirectory(
            Path.Combine(_testRoot, "Dropbox")).FullName;
        var mega = Directory.CreateDirectory(
            Path.Combine(_testRoot, "MEGA")).FullName;

        var candidates = WindowsCloudSyncFolderDiscovery.Resolve(
            [oneDrive, oneDrive],
            [googleDrive],
            [dropbox],
            [mega]);

        Assert.Collection(
            candidates,
            candidate =>
            {
                Assert.Equal("Dropbox", candidate.ProviderName);
                Assert.Equal(
                    Path.Combine(dropbox, "Sentory"),
                    candidate.FolderPath);
            },
            candidate =>
            {
                Assert.Equal("Google Drive", candidate.ProviderName);
                Assert.Equal(
                    Path.Combine(googleDrive, "Sentory"),
                    candidate.FolderPath);
            },
            candidate =>
            {
                Assert.Equal("MEGA", candidate.ProviderName);
                Assert.Equal(
                    Path.Combine(mega, "Sentory"),
                    candidate.FolderPath);
            },
            candidate =>
            {
                Assert.Equal("OneDrive", candidate.ProviderName);
                Assert.Equal(
                    Path.Combine(oneDrive, "Sentory"),
                    candidate.FolderPath);
            });
    }

    [Fact]
    public void DropboxInfoJsonSupportsPersonalAndBusinessFolders()
    {
        const string json = """
            {
              "personal": { "path": "C:\\Users\\test\\Dropbox" },
              "business": { "path": "D:\\Dropbox (Team)" }
            }
            """;

        var roots = WindowsCloudSyncFolderDiscovery
            .ResolveDropboxRootsFromInfoJson(json);

        Assert.Equal(
            ["C:\\Users\\test\\Dropbox", "D:\\Dropbox (Team)"],
            roots);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{ not-json }")]
    public void DropboxInfoJsonRejectsUnavailableAccountData(string json)
    {
        var roots = WindowsCloudSyncFolderDiscovery
            .ResolveDropboxRootsFromInfoJson(json);

        Assert.Empty(roots);
    }

    [Theory]
    [InlineData("My Drive")]
    [InlineData("내 드라이브")]
    [InlineData("マイドライブ")]
    [InlineData("我的云端硬盘")]
    public void GoogleDriveRootSupportsSentoryDisplayLanguages(
        string myDriveName)
    {
        const string root = "X:\\";

        var result = WindowsCloudSyncFolderDiscovery.ResolveGoogleMyDriveRoot(
            root,
            ["Other Computers", myDriveName]);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, myDriveName)),
            result);
    }

    [Fact]
    public void GoogleDriveRootRejectsUnknownTopLevelFolder()
    {
        var result = WindowsCloudSyncFolderDiscovery.ResolveGoogleMyDriveRoot(
            "X:\\",
            ["Shared drives", "Other Computers"]);

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
