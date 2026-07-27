namespace SchulerPark.Api.Security;

using System.Net;
using System.Net.Sockets;

/// <summary>
/// Client-IP helpers for the rate limiter and the Hangfire dashboard filter.
/// </summary>
public static class ClientNetwork
{
    /// <summary>
    /// Rate-limit partition key for a client address. IPv6 addresses are masked to
    /// their /64 prefix: providers route whole /64s (or larger) to a single
    /// subscriber, so keying on the full address would give an attacker 2^64
    /// fresh buckets and make any per-IP limit meaningless.
    /// </summary>
    public static string RateLimitPartitionKey(IPAddress? address)
    {
        if (address == null)
            return "unknown";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>
    /// True for loopback, RFC 1918 / link-local IPv4 and loopback, ULA (fc00::/7),
    /// link-local IPv6 — i.e. addresses that can only originate on this machine or
    /// the local (compose) network, never from the public internet.
    /// </summary>
    public static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        // IPv6: ULA fc00::/7, link-local fe80::/10
        return (bytes[0] & 0xFE) == 0xFC
            || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }
}
