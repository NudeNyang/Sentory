using Sentory.Infrastructure.Data;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SentorySettingsTests
{
    [Fact]
    public void MessengerDetectionDefaultsToDisabledUntilSetupCompletes()
    {
        var settings = new SentorySettings();

        Assert.False(settings.MessengerDetectionSetupCompleted);
        Assert.False(settings.DiscordSupportEnabled);
        Assert.False(settings.KakaoTalkSupportEnabled);
        Assert.False(settings.SlackSupportEnabled);
        Assert.False(settings.WhatsAppSupportEnabled);
        Assert.False(settings.TelegramSupportEnabled);
        Assert.False(settings.LineSupportEnabled);
        Assert.False(settings.WeChatSupportEnabled);
    }

    [Fact]
    public void ExistingSettingsKeepTheirMessengerChoicesAndSkipSetup()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sentory-settings-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = SentoryDataPaths.ForRoot(root);
            paths.EnsureDirectories();
            File.WriteAllText(
                paths.SettingsPath,
                """
                {
                  "DiscordSupportEnabled": false,
                  "KakaoTalkSupportEnabled": true,
                  "SlackSupportEnabled": false,
                  "WhatsAppSupportEnabled": true,
                  "TelegramSupportEnabled": false,
                  "LineSupportEnabled": true,
                  "WeChatSupportEnabled": false
                }
                """);

            var settings = new SentorySettingsStore(paths).Load();

            Assert.True(settings.MessengerDetectionSetupCompleted);
            Assert.False(settings.DiscordSupportEnabled);
            Assert.True(settings.KakaoTalkSupportEnabled);
            Assert.False(settings.SlackSupportEnabled);
            Assert.True(settings.WhatsAppSupportEnabled);
            Assert.False(settings.TelegramSupportEnabled);
            Assert.True(settings.LineSupportEnabled);
            Assert.False(settings.WeChatSupportEnabled);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SavedIncompleteMessengerSetupRemainsIncomplete()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sentory-settings-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = SentoryDataPaths.ForRoot(root);
            var store = new SentorySettingsStore(paths);
            store.Save(new SentorySettings());

            var settings = store.Load();

            Assert.False(settings.MessengerDetectionSetupCompleted);
            Assert.All(
                new[]
                {
                    settings.DiscordSupportEnabled,
                    settings.KakaoTalkSupportEnabled,
                    settings.SlackSupportEnabled,
                    settings.WhatsAppSupportEnabled,
                    settings.TelegramSupportEnabled,
                    settings.LineSupportEnabled,
                    settings.WeChatSupportEnabled
                },
                Assert.False);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

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

    [Fact]
    public void NormalizePreservesCompleteWebDavSyncConfiguration()
    {
        var deviceId = SyncDeviceIdentity.Create();
        var settings = new SentorySettings
        {
            SyncEnabled = true,
            SyncProvider = "WebDav",
            SyncWebDavEndpoint = "https://nas.example.test/webdav/Sentory/",
            SyncWebDavUsername = "sentory",
            SyncWebDavProtectedPassword = "protected-secret",
            SyncDeviceId = deviceId
        };

        settings.Normalize();

        Assert.True(settings.SyncEnabled);
        Assert.Equal("WebDav", settings.SyncProvider);
        Assert.Equal(
            "https://nas.example.test/webdav/Sentory/",
            settings.SyncWebDavEndpoint);
        Assert.Equal(deviceId, settings.SyncDeviceId);
    }

    [Theory]
    [InlineData("ftp://nas.example.test/Sentory/")]
    [InlineData("https://user:secret@nas.example.test/Sentory/")]
    [InlineData("https://nas.example.test/Sentory/?token=secret")]
    [InlineData("relative-webdav")]
    [InlineData("")]
    public void NormalizeDisablesInvalidWebDavConfiguration(string endpoint)
    {
        var settings = new SentorySettings
        {
            SyncEnabled = true,
            SyncProvider = "WebDav",
            SyncWebDavEndpoint = endpoint,
            SyncDeviceId = SyncDeviceIdentity.Create()
        };

        settings.Normalize();

        Assert.False(settings.SyncEnabled);
    }
}
