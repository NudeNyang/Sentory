using Sentory.Infrastructure.Data;

namespace Sentory.App.Tests;

public sealed class GallerySettingsSavePolicyTests
{
    [Fact]
    public void MergePreservesSettingsChangedOutsideGallery()
    {
        var current = new SentorySettings
        {
            StartWithWindows = true,
            DiscordSupportEnabled = false,
            KakaoTalkSupportEnabled = false,
            SlackSupportEnabled = false,
            WhatsAppSupportEnabled = false,
            LineSupportEnabled = false,
            AutoCleanupDays = 90,
            AutoFavoriteEnabled = true,
            AutoFavoriteCopyThreshold = 5,
            AutoFavoriteChangedAt = DateTimeOffset.Parse("2026-07-28T01:00:00Z"),
            LastUpdateCheckAt = DateTimeOffset.Parse("2026-07-28T02:00:00Z"),
            SyncEnabled = true,
            SyncFolderPath = @"C:\Cloud\Sentory",
            SyncDeviceId = "11111111111111111111111111111111",
            SyncStorageVersion = SentorySettings.CurrentSyncStorageVersion,
            SyncMigrationDeviceId = "22222222222222222222222222222222",
            SyncStoreId = "33333333333333333333333333333333"
        };
        var staleGallerySettings = new SentorySettings
        {
            StartWithWindows = false,
            SyncEnabled = false,
            SyncFolderPath = @"C:\Old\Sentory",
            SyncDeviceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            SyncStoreId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        };

        var merged = GallerySettingsSavePolicy.Merge(
            current,
            staleGallerySettings);

        Assert.True(merged.StartWithWindows);
        Assert.False(merged.DiscordSupportEnabled);
        Assert.False(merged.KakaoTalkSupportEnabled);
        Assert.False(merged.SlackSupportEnabled);
        Assert.False(merged.WhatsAppSupportEnabled);
        Assert.False(merged.LineSupportEnabled);
        Assert.Equal(90, merged.AutoCleanupDays);
        Assert.True(merged.AutoFavoriteEnabled);
        Assert.Equal(5, merged.AutoFavoriteCopyThreshold);
        Assert.Equal(current.AutoFavoriteChangedAt, merged.AutoFavoriteChangedAt);
        Assert.Equal(current.LastUpdateCheckAt, merged.LastUpdateCheckAt);
        Assert.True(merged.SyncEnabled);
        Assert.Equal(current.SyncFolderPath, merged.SyncFolderPath);
        Assert.Equal(current.SyncDeviceId, merged.SyncDeviceId);
        Assert.Equal(current.SyncStorageVersion, merged.SyncStorageVersion);
        Assert.Equal(current.SyncMigrationDeviceId, merged.SyncMigrationDeviceId);
        Assert.Equal(current.SyncStoreId, merged.SyncStoreId);
    }

    [Fact]
    public void MergeAppliesPreferencesOwnedByGallery()
    {
        var current = new SentorySettings();
        var gallerySettings = new SentorySettings
        {
            SortMode = "Oldest",
            FilterDateRange = "Week",
            FilterSourceApps = ["Discord", "Line"],
            IsDarkTheme = true,
            ThemeMode = "Dark",
            Language = "ja-JP",
            WindowLeft = 100,
            WindowTop = 120,
            WindowWidth = 1280,
            WindowHeight = 800,
            WindowMaximized = true
        };

        var merged = GallerySettingsSavePolicy.Merge(
            current,
            gallerySettings);

        Assert.Equal("Oldest", merged.SortMode);
        Assert.Equal("Week", merged.FilterDateRange);
        Assert.Equal(["Discord", "Line"], merged.FilterSourceApps);
        Assert.NotSame(gallerySettings.FilterSourceApps, merged.FilterSourceApps);
        Assert.True(merged.IsDarkTheme);
        Assert.Equal("Dark", merged.ThemeMode);
        Assert.Equal("ja-JP", merged.Language);
        Assert.Equal(100, merged.WindowLeft);
        Assert.Equal(120, merged.WindowTop);
        Assert.Equal(1280, merged.WindowWidth);
        Assert.Equal(800, merged.WindowHeight);
        Assert.True(merged.WindowMaximized);
    }
}
