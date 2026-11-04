using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// POST /api/push/subscribe stores an Endpoint that the server later POSTs to
/// (VAPID-signed, via the WebPush client). Any signed-in user controls that URL,
/// so the server must refuse endpoints that point into its own network: a
/// loopback, private or link-local address (or localhost) is never a push
/// service and would turn /api/push/test into an internal port scanner (SSRF).
/// </summary>
[Collection("Integration")]
public class PushSubscriptionEndpointTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PushSubscriptionEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage Authed(HttpMethod m, string url, string token, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Theory]
    [InlineData("https://127.0.0.1:1/")]
    [InlineData("https://localhost/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://10.0.0.1/")]
    [InlineData("https://192.168.1.1:8443/internal")]
    public async Task Subscribe_RejectsInternalEndpointsAndNeverPushesToThem(string endpoint)
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);

        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/subscribe", auth.AccessToken, new
        {
            endpoint,
            p256dh = "BPublicKeyBytes",
            auth = "authSecret"
        }));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a push endpoint on a loopback, private or link-local host is never a push service and must not be stored");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PushSubscriptions.AnyAsync(p => p.Endpoint == endpoint)).Should().BeFalse(
                "a rejected endpoint must not be persisted");
        }

        // Even if something slipped through, the server must not make an outbound
        // request to an internal host on behalf of the user.
        await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/test", auth.AccessToken));
        _factory.Pushes.Sent.Should().NotContain(s => s.Endpoint == endpoint);
    }
}
