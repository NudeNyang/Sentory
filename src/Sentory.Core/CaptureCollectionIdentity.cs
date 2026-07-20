using System.Security.Cryptography;
using System.Text;

namespace Sentory.Core;

public static class CaptureCollectionIdentity
{
    public static string CreateSignature(
        IEnumerable<CollectionMemberCaptureRequest> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var canonical = string.Join('\n', members
            .Select(member => $"{member.Kind}:{member.NormalizedKey}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
