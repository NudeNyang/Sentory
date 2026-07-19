using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("v0.9.1-beta", "0.9.0-beta")]
    [InlineData("1.0.0", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1")]
    public void NewerVersionComparesGreater(string newer, string older)
    {
        Assert.True(SemanticVersion.TryParse(newer, out var left));
        Assert.True(SemanticVersion.TryParse(older, out var right));
        Assert.True(left.CompareTo(right) > 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("release")]
    public void RejectsInvalidVersions(string? value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));
}
