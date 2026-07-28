using Sentory.Infrastructure.Data;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SentorySettingsTests
{
    [Fact]
    public void LanguageDefaultsToAutomatic()
    {
        var settings = new SentorySettings();

        Assert.Equal(
            SentorySettings.AutomaticLanguage,
            settings.Language);
    }

    [Fact]
    public void MigratesLegacyKoreanLanguageDefaultToAutomatic()
    {
        var settings = new SentorySettings
        {
            Language = "ko-KR",
            LanguageSettingVersion = 0
        };

        settings.Normalize();

        Assert.Equal(
            SentorySettings.AutomaticLanguage,
            settings.Language);
        Assert.Equal(
            SentorySettings.CurrentLanguageSettingVersion,
            settings.LanguageSettingVersion);
    }

    [Fact]
    public void PreservesExplicitKoreanLanguageSelection()
    {
        var settings = new SentorySettings
        {
            Language = "ko-KR",
            LanguageSettingVersion =
                SentorySettings.CurrentLanguageSettingVersion
        };

        settings.Normalize();

        Assert.Equal("ko-KR", settings.Language);
    }

    [Fact]
    public void AutoFavoriteDefaultsToEnabledAtThreeCopies()
    {
        var settings = new SentorySettings();

        Assert.True(settings.AutoFavoriteEnabled);
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
    public void MigratesUntouchedDisabledAutoFavoriteDefaultToEnabled()
    {
        var settings = new SentorySettings
        {
            AutoFavoriteEnabled = false,
            AutoFavoriteCopyThreshold =
                SentorySettings.DefaultAutoFavoriteCopyThreshold,
            AutoFavoriteChangedAt = null
        };

        settings.Normalize();

        Assert.True(settings.AutoFavoriteEnabled);
        Assert.Null(settings.AutoFavoriteChangedAt);
    }

    [Fact]
    public void PreservesExplicitlyDisabledAutoFavoriteSetting()
    {
        var changedAt = DateTimeOffset.Parse("2026-07-28T06:00:00Z");
        var settings = new SentorySettings
        {
            AutoFavoriteEnabled = false,
            AutoFavoriteCopyThreshold =
                SentorySettings.DefaultAutoFavoriteCopyThreshold,
            AutoFavoriteChangedAt = changedAt
        };

        settings.Normalize();

        Assert.False(settings.AutoFavoriteEnabled);
        Assert.Equal(changedAt, settings.AutoFavoriteChangedAt);
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

    [Fact]
    public void NormalizePreservesValidSyncStoreIdentity()
    {
        var settings = new SentorySettings
        {
            SyncStoreId = "0123456789abcdef0123456789abcdef"
        };

        settings.Normalize();

        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            settings.SyncStoreId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-store-id")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public void NormalizeRejectsInvalidSyncStoreIdentity(string storeId)
    {
        var settings = new SentorySettings
        {
            SyncStoreId = storeId
        };

        settings.Normalize();

        Assert.Null(settings.SyncStoreId);
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
