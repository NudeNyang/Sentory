namespace Sentory.App.Tests;

public sealed class OcrRuntimePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData(" 1 ")]
    public void OcrRemainsEnabledWithoutExplicitOptOut(string? value)
    {
        Assert.False(OcrRuntimePolicy.IsDisabled(value));
    }

    [Fact]
    public void OcrCanBeDisabledForIncompatibleQaEnvironments()
    {
        Assert.True(OcrRuntimePolicy.IsDisabled("1"));
    }
}
