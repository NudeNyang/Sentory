using System.Text;

namespace Sentory.Core;

public static class UrlNormalizer
{
    private static readonly HashSet<string> TrackingParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "fbclid",
            "gclid",
            "dclid",
            "msclkid",
            "mc_cid",
            "mc_eid",
            "igshid",
            "ref_src",
            "ref_url",
            "vero_conv",
            "vero_id",
            "_hsenc",
            "_hsmi"
        };

    public static bool TryNormalize(
        string? candidate,
        out NormalizedUrl normalized)
    {
        normalized = null!;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = TrimSurroundingPunctuation(candidate.Trim());
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == "http" && builder.Port == 80) ||
            (builder.Scheme == "https" && builder.Port == 443))
        {
            builder.Port = -1;
        }

        builder.Path = string.IsNullOrEmpty(uri.AbsolutePath)
            ? "/"
            : uri.AbsolutePath;
        builder.Query = NormalizeQuery(uri.Query);

        var value = builder.Uri.AbsoluteUri;
        normalized = new NormalizedUrl(
            trimmed,
            value,
            builder.Host);
        return true;
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var pairs = new List<(string Key, string Value)>();
        foreach (var segment in query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            var rawKey = separator >= 0
                ? segment[..separator]
                : segment;
            var rawValue = separator >= 0
                ? segment[(separator + 1)..]
                : string.Empty;
            var key = Uri.UnescapeDataString(rawKey.Replace("+", " "));
            if (IsTrackingParameter(key))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(rawValue.Replace("+", " "));
            pairs.Add((key, value));
        }

        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var pair in pairs
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Value, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(pair.Key));
            if (pair.Value.Length > 0)
            {
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
            }
        }

        return builder.ToString();
    }

    private static bool IsTrackingParameter(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
        TrackingParameters.Contains(key);

    private static string TrimSurroundingPunctuation(string value)
    {
        while (value.Length > 0 &&
               value[0] is '<' or '(' or '[' or '{' or '"' or '\'')
        {
            value = value[1..];
        }

        while (value.Length > 0 &&
               value[^1] is '>' or ')' or ']' or '}' or '"' or '\'' or
                   ',' or '.' or ';' or '!')
        {
            value = value[..^1];
        }

        return value;
    }
}
