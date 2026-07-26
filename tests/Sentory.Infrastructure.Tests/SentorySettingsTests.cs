using Sentory.Infrastructure.Data;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SentorySettingsTests
{
    [Fact]
    public void AutoFavoriteDefaultsToDisabledAtThreeCopies()
    {
        var settings = new SentorySettings();

        Assert.False(settings.AutoFavoriteEnabled);
        Assert.Equal(
            SentorySettings.DefaultAutoFavoriteCopyThreshold,
            settings.AutoFavoriteCopyThreshold);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void NormalizeResetsUnsupportedAutoFavoriteThreshold(
        int copyThreshold)
    {
        var settings = new SentorySettings
        {
            AutoFavoriteEnabled = true,
            AutoFavoriteCopyThreshold = copyThreshold
        };

        settings.Normalize();

        Assert.True(settings.AutoFavoriteEnabled);
        Assert.Equal(
            SentorySettings.DefaultAutoFavoriteCopyThreshold,
            settings.AutoFavoriteCopyThreshold);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void NormalizePreservesSupportedAutoFavoriteThreshold(
        int copyThreshold)
    {
        var settings = new SentorySettings
        {
            AutoFavoriteCopyThreshold = copyThreshold
        };

        settings.Normalize();

        Assert.Equal(
            copyThreshold,
            settings.AutoFavoriteCopyThreshold);
    }

    [Fact]
    public void SyncDefaultsToDisabledWithoutFolderOrDevice()
    {
        var settings = new SentorySettings();

        settings.Normalize();

        Assert.False(settings.SyncEnabled);
        Assert.Null(settings.SyncFolderPath);
        Assert.Null(settings.SyncDeviceId);
        Assert.Equal(1, settings.SyncStorageVersion);
    }

    [Fact]
    public void NormalizePreservesCompleteSyncConfiguration()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "Sentory.Sync.Settings");
        var deviceId = SyncDeviceIdentity.Create();
        var settings = new SentorySettings
        {
            SyncEnabled = true,
            SyncFolderPath = folder,
            SyncDeviceId = deviceId
        };

        settings.Normalize();

        Assert.True(settings.SyncEnabled);
        Assert.Equal(Path.GetFullPath(folder), settings.SyncFolderPath);
        Assert.Equal(deviceId, settings.SyncDeviceId);
        Assert.Equal(1, settings.SyncStorageVersion);
    }

    [Fact]
    public void NormalizePreservesReadableStorageMigrationState()
    {
        var settings = new SentorySettings
        {
            SyncStorageVersion = SentorySettings.CurrentSyncStorageVersion,
            SyncMigrationDeviceId = SyncDeviceIdentity.Create()
        };

        settings.Normalize();

        Assert.Equal(
            SentorySettings.CurrentSyncStorageVersion,
            settings.SyncStorageVersion);
        Assert.NotNull(settings.SyncMigrationDeviceId);
    }

    [Theory]
    [InlineData(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("relative-folder", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("C:\\Cloud", "invalid-device")]
    public void NormalizeDisablesIncompleteSyncConfiguration(
        string? folder,
        string deviceId)
    {
        var settings = new SentorySettings
        {
            SyncEnabled = true,
            SyncFolderPath = folder,
            SyncDeviceId = deviceId
        };

        settings.Normalize();

        Assert.False(settings.SyncEnabled);
    }
}
