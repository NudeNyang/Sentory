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
}
