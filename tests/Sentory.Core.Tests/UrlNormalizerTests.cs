using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class UrlNormalizerTests
{
    [Fact]
    public void RemovesTrackingParametersAndFragment()
    {
        var success = UrlNormalizer.TryNormalize(
            "https://Example.com/path?utm_source=test&b=2&a=1#section",
            out var result);

        Assert.True(success);
        Assert.Equal(
            "https://example.com/path?a=1&b=2",
            result.Value);
        Assert.Equal("example.com", result.Domain);
    }

    [Theory]
    [InlineData("file:///c:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("hello")]
    [InlineData("")]
    public void RejectsNonHttpUrls(string value)
    {
        Assert.False(UrlNormalizer.TryNormalize(value, out _));
    }

    [Fact]
    public void TrimsChatPunctuation()
    {
        Assert.True(UrlNormalizer.TryNormalize(
            "(https://example.com/test).",
            out var result));

        Assert.Equal("https://example.com/test", result.Value);
    }
}
