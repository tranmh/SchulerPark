namespace SchulerPark.Infrastructure.Services;

using System.Net;
using System.Net.Sockets;

/// <summary>
/// Decides whether a push subscription Endpoint may be contacted by the server.
/// The URL is supplied by the signed-in user and the server later POSTs to it, so
/// anything pointing into our own network (loopback, private, link-local, the
/// cloud metadata address) is refused: no real push service lives there.
/// </summary>
public static class PushEndpointPolicy
{
    public static bool IsAllowed(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && IsAllowed(uri);

    public static bool IsAllowed(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.IdnHost;
        if (string.IsNullOrWhiteSpace(host)) return false;

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            return IPAddress.TryParse(host, out var ip) && !IsInternal(ip);

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsInternal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;

        var b = ip.GetAddressBytes();
        return b[0] == 10                                   // 10.0.0.0/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
            || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 (incl. metadata)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);  // 100.64.0.0/10 (CGNAT)
    }
}
