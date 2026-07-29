using System.Security.Cryptography;
using System.Text;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal static class MessengerCandidateDuplicatePolicy
{
    private static readonly TimeSpan DuplicateBurstWindow =
        TimeSpan.FromMilliseconds(500);

    public static string CreatePayloadSignature(
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images)
    {
        var members = urls
            .Select(url => $"url:{url.Value}")
            .Concat(images.Select(image =>
                $"image:{image.Sha256.ToLowerInvariant()}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var payload = string.Join('\n', members);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static bool IsDuplicateBurst(
        string existingContextHash,
        DateTimeOffset existingOccurredAt,
        string existingPayloadSignature,
        string contextHash,
        DateTimeOffset occurredAt,
        string payloadSignature)
    {
        var elapsed = occurredAt - existingOccurredAt;
        return string.Equals(
                   existingContextHash,
                   contextHash,
                   StringComparison.Ordinal) &&
               string.Equals(
                   existingPayloadSignature,
                   payloadSignature,
                   StringComparison.Ordinal) &&
               elapsed >= TimeSpan.Zero &&
               elapsed <= DuplicateBurstWindow;
    }
}
