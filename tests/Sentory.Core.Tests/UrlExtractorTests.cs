using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class UrlExtractorTests
{
    [Fact]
    public void ExtractsOnlyUniqueNormalizedUrls()
    {
        var results = UrlExtractor.Extract(
            """
            first https://example.com/a?utm_source=x
            second https://example.com/a
            ignored ordinary text
            """);

        var result = Assert.Single(results);
        Assert.Equal("https://example.com/a", result.Value);
    }

    [Fact]
    public void ReturnsEmptyForGeneralText()
    {
        Assert.Empty(UrlExtractor.Extract("카카오톡 일반 메시지"));
    }
}
