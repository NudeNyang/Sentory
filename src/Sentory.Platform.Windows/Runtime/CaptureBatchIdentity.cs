using System.Security.Cryptography;
using System.Text;

namespace Sentory.Platform.Windows.Runtime;

internal static class CaptureBatchIdentity
{
    public static Guid ForImage(Guid pasteEventId, string imageHash)
    {
        var input = new byte[16 + Encoding.UTF8.GetByteCount(imageHash)];
        pasteEventId.TryWriteBytes(input);
        Encoding.UTF8.GetBytes(imageHash, input.AsSpan(16));
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        return new Guid(digest[..16]);
    }
}

internal static class DiscordSendSignalPolicy
{
    private static readonly TimeSpan ImageAssociationWindow =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UrlAssociationWindow =
        TimeSpan.FromSeconds(10);

    public static TimeSpan Retention => ImageAssociationWindow;

    public static bool CanAssociate(
        DateTimeOffset pastedAt,
        DateTimeOffset sentAt,
        DateTimeOffset observedAt,
        bool isImage) =>
        sentAt >= pastedAt &&
        observedAt - sentAt <=
        (isImage ? ImageAssociationWindow : UrlAssociationWindow);
}
