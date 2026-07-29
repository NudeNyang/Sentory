using Sentory.Core;

namespace Sentory.App.Tests;

public sealed class AutoFavoritePolicyTests
{
    [Theory]
    [InlineData(ContentKind.Url)]
    [InlineData(ContentKind.Image)]
    [InlineData(ContentKind.Collection)]
    public void AddsSupportedItemAtOrAboveThreshold(ContentKind kind)
    {
        Assert.True(AutoFavoritePolicy.ShouldAdd(
            kind,
            isFavorite: false,
            copyCount: 3,
            enabled: true,
            threshold: 3));
        Assert.True(AutoFavoritePolicy.ShouldAdd(
            kind,
            isFavorite: false,
            copyCount: 4,
            enabled: true,
            threshold: 3));
    }

    [Theory]
    [InlineData(ContentKind.File)]
    public void IgnoresUnsupportedItemKinds(ContentKind kind)
    {
        Assert.False(AutoFavoritePolicy.ShouldAdd(
            kind,
            isFavorite: false,
            copyCount: 3,
            enabled: true,
            threshold: 3));
    }

    [Fact]
    public void RequiresEnabledSettingThresholdAndNonFavoriteItem()
    {
        Assert.False(AutoFavoritePolicy.ShouldAdd(
            ContentKind.Url,
            isFavorite: false,
            copyCount: 3,
            enabled: false,
            threshold: 3));
        Assert.False(AutoFavoritePolicy.ShouldAdd(
            ContentKind.Url,
            isFavorite: false,
            copyCount: 2,
            enabled: true,
            threshold: 3));
        Assert.False(AutoFavoritePolicy.ShouldAdd(
            ContentKind.Url,
            isFavorite: true,
            copyCount: 3,
            enabled: true,
            threshold: 3));
    }
}
