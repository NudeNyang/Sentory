using System.Net;
using System.Net.Sockets;

namespace Sentory.Infrastructure.Links;

internal interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}

internal sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

internal static class LinkPreviewUriPolicy
{
    public static async Task<bool> IsAllowedAsync(
        Uri uri,
        IHostAddressResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (!uri.IsDefaultPort && uri.Port is not (80 or 443)) ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var literalAddress))
        {
            return IsPublic(literalAddress);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolver.ResolveAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (SocketException)
        {
            return false;
        }

        return addresses.Length > 0 && addresses.All(IsPublic);
    }

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 168 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal &&
                   !address.IsIPv6Multicast &&
                   !address.IsIPv6SiteLocal &&
                   (bytes[0] & 0xFE) != 0xFC;
        }

        return false;
    }
}
