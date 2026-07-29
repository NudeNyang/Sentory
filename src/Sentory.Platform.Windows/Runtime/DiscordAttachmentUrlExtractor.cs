using System.Text.RegularExpressions;

namespace Sentory.Platform.Windows.Runtime;

internal static partial class DiscordAttachmentUrlExtractor
{
    [GeneratedRegex(
        @"https://(?:cdn\.discordapp\.com|media\.discordapp\.net)/attachments/[^\s\""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentUrlPattern();

    public static IReadOnlyList<string> Extract(
        IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => AttachmentUrlPattern().Matches(value!)
                .Select(match => match.Value.TrimEnd(')', ']', '}', ',', '.')))
            .Where(IsAllowedAttachmentUrl)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool IsAllowedAttachmentUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.AbsolutePath.StartsWith(
                "/attachments/",
                StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
                   uri.Host,
                   "cdn.discordapp.com",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   uri.Host,
                   "media.discordapp.net",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> SelectNew(
        IEnumerable<string> candidateUrls,
        IEnumerable<string> knownUrls)
    {
        var knownIdentities = knownUrls
            .Select(CreateIdentity)
            .Where(identity => identity is not null)
            .Select(identity => identity!)
            .ToHashSet(StringComparer.Ordinal);
        return SelectNewAgainstIdentities(candidateUrls, knownIdentities);
    }

    internal static IReadOnlyList<string> SelectNewAgainstIdentities(
        IEnumerable<string> candidateUrls,
        IReadOnlySet<string> knownIdentities)
    {
        var selected = new List<string>();
        var selectedIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in candidateUrls)
        {
            var identity = CreateIdentity(url);
            if (identity is null ||
                knownIdentities.Contains(identity) ||
                !selectedIdentities.Add(identity))
            {
                continue;
            }

            selected.Add(url);
        }

        return selected;
    }

    internal static string? CreateIdentity(string value)
    {
        if (!IsAllowedAttachmentUrl(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.AbsolutePath;
    }
}
