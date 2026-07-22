using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class AuthRateLimitAndCacheTests
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthRateLimitAndCacheTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string NewEmail(string label) => $"{label}-{Guid.NewGuid():N}@schuler.de";

    // ---- Bug #48: the strict "auth" limiter must apply to login (a POST that can be brute-forced),
    //      NOT to the high-frequency read endpoints like /config. ----
    [Fact]
    public async Task Login_is_rate_limited_but_config_is_not()
    {
        // A dedicated app with a tiny auth limit and a large global one, so the difference is
        // attributable to endpoint scoping, not the global limiter.
        var client = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimit:AuthPermitLimit", "2");
            b.UseSetting("RateLimit:GlobalPermitLimit", "1000000");
        }).CreateClient();

        // /config carries only the global limiter → never 429, even hammered.
        for (var i = 0; i < 6; i++)
        {
            var cfg = await client.GetAsync("/api/auth/config");
            cfg.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        // /login carries the strict "auth" limiter (permit 2/min) → later attempts get 429.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "nobody@schuler.de", password = "wrong-password" });
            statuses.Add(res.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the strict auth limiter must fire on repeated login attempts");
    }

    // ---- Bug #49: the per-request account-active check must be cached, not queried on every
    //      authenticated call. Proof: a positive result survives a mid-TTL soft-delete — which is
    //      only possible if the second request did NOT re-query the DB. ----
    [Fact]
    public async Task Active_user_check_is_cached_across_requests()
    {
        var client = _factory.CreateClient();
        var email = NewEmail("cache");
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, client, email);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // First authed call: OnTokenValidated queries the DB (user active) and caches it (30s TTL).
        var first = await client.GetAsync("/api/auth/me");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Soft-delete the user directly in the DB.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            user.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Second authed call with the SAME token, well within the 30s TTL.
        // GREEN (cached): auth still passes → not 401 (uses the cached "active" result).
        // RED (no cache): a fresh DB query sees DeletedAt != null → context.Fail() → 401.
        var second = await client.GetAsync("/api/auth/me");
        second.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
