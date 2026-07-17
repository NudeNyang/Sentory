using System.Text.RegularExpressions;

namespace Sentory.Core;

public static partial class UrlExtractor
{
    public static IReadOnlyList<NormalizedUrl> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var results = new List<NormalizedUrl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in UrlPattern().Matches(text))
        {
            if (!UrlNormalizer.TryNormalize(match.Value, out var normalized) ||
                !seen.Add(normalized.Value))
            {
                continue;
            }

            results.Add(normalized);
        }

        return results;
    }

    [GeneratedRegex(
        @"https?://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}
