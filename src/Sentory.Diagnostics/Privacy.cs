using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sentory.Diagnostics;

public static partial class Privacy
{
    public static string SafeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return SafeIdentifierPattern().IsMatch(value)
            ? value
            : "<redacted>";
    }

    public static string LengthBucket(string? value) =>
        LengthBucket(value?.Length ?? 0);

    public static string LengthBucket(int length) => length switch
    {
        0 => "empty",
        <= 4 => "1-4",
        <= 16 => "5-16",
        <= 64 => "17-64",
        <= 256 => "65-256",
        _ => "257+"
    };

    public static string RuntimeIdHash(int[]? runtimeId)
    {
        if (runtimeId is null || runtimeId.Length == 0)
        {
            return "unavailable";
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join('.', runtimeId));
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z0-9_.:#-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierPattern();
}
