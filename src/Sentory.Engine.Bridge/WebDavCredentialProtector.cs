using System.Security.Cryptography;
using System.Text;

namespace Sentory.Engine.Bridge;

internal static class WebDavCredentialProtector
{
    private static readonly byte[] Entropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("Sentory WebDAV credential v1"));

    public static string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = ProtectedData.Unprotect(
            Convert.FromBase64String(value),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
