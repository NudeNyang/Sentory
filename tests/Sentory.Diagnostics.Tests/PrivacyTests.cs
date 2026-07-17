namespace Sentory.Diagnostics.Tests;

public sealed class PrivacyTests
{
    [Theory]
    [InlineData("Chrome_RenderWidgetHostHWND", "Chrome_RenderWidgetHostHWND")]
    [InlineData("message-input", "message-input")]
    [InlineData("", "")]
    [InlineData("private user name", "<redacted>")]
    [InlineData("한글이름", "<redacted>")]
    public void SafeIdentifierOnlyKeepsSelectorShapedValues(
        string input,
        string expected)
    {
        Assert.Equal(expected, Privacy.SafeIdentifier(input));
    }

    [Fact]
    public void RuntimeIdHashIsStableButDoesNotExposeRawId()
    {
        var first = Privacy.RuntimeIdHash([42, 7, 123456]);
        var second = Privacy.RuntimeIdHash([42, 7, 123456]);

        Assert.Equal(first, second);
        Assert.DoesNotContain("123456", first);
        Assert.Equal(24, first.Length);
    }

    [Theory]
    [InlineData(0, "empty")]
    [InlineData(4, "1-4")]
    [InlineData(16, "5-16")]
    [InlineData(64, "17-64")]
    [InlineData(256, "65-256")]
    [InlineData(257, "257+")]
    public void LengthBucketDoesNotRetainExactLargeLengths(int length, string expected)
    {
        Assert.Equal(expected, Privacy.LengthBucket(length));
    }
}
