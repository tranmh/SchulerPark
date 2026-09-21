using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// POST /api/push/test — the manual end-to-end push check exposed on the Profile
/// page. The Web Push transport is <see cref="RecordingPushSender"/>, so these
/// tests cover subscription lookup, fan-out, result reporting and 410 pruning
/// without a browser or a real push service.
/// </summary>
[Collection("Integration")]
public class PushTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PushTests(CustomWebApplicationFactory factory)
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

    private static string NewEndpoint() => $"https://push.example.test/send/{Guid.NewGuid():N}";

    private async Task SubscribeAsync(string token, string endpoint)
    {
        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/subscribe", token, new
        {
            endpoint,
            p256dh = "BPublicKeyBytes",
            auth = "authSecret"
        }));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_RequiresAuthentication()
    {
        var res = await _client.PostAsync("/api/push/test", null);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_WithoutSubscription_Returns404()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);

        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/test", auth.AccessToken));

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("No push subscription");
    }

    [Fact]
    public async Task Test_WithSubscriptions_DeliversToEveryDeviceAndReportsCounts()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var laptop = NewEndpoint();
        var phone = NewEndpoint();
        await SubscribeAsync(auth.AccessToken, laptop);
        await SubscribeAsync(auth.AccessToken, phone);

        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/test", auth.AccessToken));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await res.Content.ReadFromJsonAsync<TestResponse>();
        result.Should().Be(new TestResponse(2, 2, 0, 0));

        var sent = _factory.Pushes.Sent.Where(s => s.Endpoint == laptop || s.Endpoint == phone).ToList();
        sent.Select(s => s.Endpoint).Should().BeEquivalentTo([laptop, phone]);

        // Payload is what sw.ts's push handler reads: title/body/url (camelCase).
        using var payload = JsonDocument.Parse(sent[0].Payload);
        payload.RootElement.GetProperty("title").GetString().Should().ContainEquivalentOf("test"); // "Testbenachrichtigung" (de) or "test notification" (en)
        payload.RootElement.GetProperty("body").GetString().Should().NotBeNullOrEmpty();
        payload.RootElement.GetProperty("url").GetString().Should().Be("/profile");
    }

    [Fact]
    public async Task Test_OnlyTargetsTheCallersOwnSubscriptions()
    {
        var alice = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var bob = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var bobsDevice = NewEndpoint();
        await SubscribeAsync(bob.AccessToken, bobsDevice);

        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/test", alice.AccessToken));

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Pushes.Sent.Should().NotContain(s => s.Endpoint == bobsDevice);
    }

    [Fact]
    public async Task Test_ExpiredSubscription_IsPrunedAndReportedAs502()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var stale = NewEndpoint();
        await SubscribeAsync(auth.AccessToken, stale);
        _factory.Pushes.GoneEndpoints[stale] = 1;

        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/test", auth.AccessToken));

        // Nothing was delivered, so the caller must not be told "sent".
        res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("1 expired and removed");
        // The actual row deletion (ExecuteDelete) is not supported by the InMemory
        // provider used here; PushPruningDbTests covers it against real PostgreSQL.
    }

    [Fact]
    public async Task Test_PartialFailure_StillReports200WithCounts()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var healthy = NewEndpoint();
        var broken = NewEndpoint();
        await SubscribeAsync(auth.AccessToken, healthy);
        await SubscribeAsync(auth.AccessToken, broken);
        _factory.Pushes.FailingEndpoints[broken] = 1;

        var res = await _client.SendAsync(Authed(HttpMethod.Post, "/api/push/test", auth.AccessToken));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await res.Content.ReadFromJsonAsync<TestResponse>();
        result.Should().Be(new TestResponse(2, 1, 0, 1));

        // A transient failure must NOT delete the subscription — only a 410 does.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushSubscriptions.AnyAsync(p => p.Endpoint == broken)).Should().BeTrue();
    }

    private record TestResponse(int Subscriptions, int Delivered, int Removed, int Failed);
}
