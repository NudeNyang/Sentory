using System.Net;
using System.Text.RegularExpressions;

namespace Sentory.Infrastructure.Links;

internal sealed record ParsedLinkPreview(
    string? Title,
    string? Description,
    Uri? SiteIconUri,
    Uri? PreviewImageUri);

internal static partial class LinkPreviewHtmlParser
{
    public static ParsedLinkPreview Parse(string html, Uri pageUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageUri);

        var metadata = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MetaTagRegex().Matches(html))
        {
            var attributes = ReadAttributes(match.Value);
            if ((!attributes.TryGetValue("property", out var key) &&
                 !attributes.TryGetValue("name", out key)) ||
                !attributes.TryGetValue("content", out var content) ||
                string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            metadata.TryAdd(key.Trim(), content.Trim());
        }

        var resolutionBase = pageUri;
        var baseMatch = BaseTagRegex().Match(html);
        if (baseMatch.Success &&
            ReadAttributes(baseMatch.Value).TryGetValue("href", out var baseHref) &&
            TryResolveHttpUri(pageUri, baseHref, out var parsedBase))
        {
            resolutionBase = parsedBase;
        }

        string? iconHref = null;
        foreach (Match match in LinkTagRegex().Matches(html))
        {
            var attributes = ReadAttributes(match.Value);
            if (!attributes.TryGetValue("rel", out var rel) ||
                !rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => value.Contains(
                        "icon",
                        StringComparison.OrdinalIgnoreCase)) ||
                !attributes.TryGetValue("href", out var href))
            {
                continue;
            }

            iconHref = href;
            break;
        }

        var title = FirstNonEmpty(
            Get(metadata, "og:title"),
            Get(metadata, "twitter:title"),
            TitleTagRegex().Match(html) is { Success: true } titleMatch
                ? titleMatch.Groups["value"].Value
                : null);
        var description = FirstNonEmpty(
            Get(metadata, "og:description"),
            Get(metadata, "twitter:description"),
            Get(metadata, "description"));
        var imageHref = FirstNonEmpty(
            Get(metadata, "og:image:secure_url"),
            Get(metadata, "og:image"),
            Get(metadata, "twitter:image"));

        return new ParsedLinkPreview(
            CleanText(title, 200),
            CleanText(description, 500),
            TryResolveHttpUri(resolutionBase, iconHref, out var iconUri)
                ? iconUri
                : null,
            TryResolveHttpUri(resolutionBase, imageHref, out var imageUri)
                ? imageUri
                : null);
    }

    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(tag))
        {
            var value = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["bare"].Value;
            attributes[match.Groups["name"].Value] =
                WebUtility.HtmlDecode(value);
        }

        return attributes;
    }

    private static string? Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? CleanText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var normalized = WhitespaceRegex().Replace(decoded, " ").Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd();
    }

    private static bool TryResolveHttpUri(
        Uri baseUri,
        string? value,
        out Uri result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(baseUri, WebUtility.HtmlDecode(value), out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        result = uri;
        return true;
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex(@"<base\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseTagRegex();

    [GeneratedRegex(
        @"<title\b[^>]*>(?<value>.*?)</title\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTagRegex();

    [GeneratedRegex(
        """(?<name>[\w:-]+)\s*=\s*(?:"(?<double>[^"]*)"|'(?<single>[^']*)'|(?<bare>[^\s>]+))""",
        RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
