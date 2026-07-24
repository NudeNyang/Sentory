using Sentory.Infrastructure.Data;

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
}
