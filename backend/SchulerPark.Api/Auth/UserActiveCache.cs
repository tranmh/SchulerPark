namespace SchulerPark.Api.Auth;

using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Central definition of the per-user "account active" cache (Bug #49). The JWT
/// <c>OnTokenValidated</c> handler caches the <c>DeletedAt</c> check here so it does not hit the
/// DB on every authenticated request. Account-lifecycle changes (disable / enable / delete) MUST
/// evict the entry so revocation and reinstatement take effect immediately (Bug #4) rather than
/// being delayed by the cache TTL.
/// </summary>
public static class UserActiveCache
{
    /// <summary>Cache TTL for the account-active check.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public static string Key(Guid userId) => $"user-active:{userId}";

    public static void Evict(IMemoryCache cache, Guid userId) => cache.Remove(Key(userId));
}
