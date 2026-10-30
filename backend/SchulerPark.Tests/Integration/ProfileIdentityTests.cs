using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>Phase 21: users edit first/last name (DisplayName derived); company is admin-only.</summary>
[Collection("Integration")]
public class ProfileIdentityTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProfileIdentityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpRequestMessage Authed(HttpMethod m, string url, string token, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task UpdateProfile_RecomputesDisplayName_AndIgnoresCompany()
    {
        var email = $"ident-{Guid.NewGuid():N}@schuler.de";
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email, "Erika Muster");

        var response = await _client.SendAsync(Authed(HttpMethod.Put, "/api/profile", auth.AccessToken, new
        {
            firstName = "Erika-Maria",
            lastName = "Mustermann",
            company = "Hacked Ltd",          // not part of the contract; must be ignored
            carLicensePlate = (string?)null,
            preferredLocationId = (Guid?)null,
            preferredSlotId = (Guid?)null
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AuthTestHelper.AuthUserDto>();
        dto!.DisplayName.Should().Be("Erika-Maria Mustermann");
        dto.Company.Should().Be(AuthTestHelper.DefaultCompany);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Email == email);
        user.DisplayName.Should().Be("Erika-Maria Mustermann");
        user.Company.Should().Be(AuthTestHelper.DefaultCompany);
    }

    [Fact]
    public async Task UpdateProfile_WithLegacyDisplayNameOnly_Returns400()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);

        var response = await _client.SendAsync(Authed(HttpMethod.Put, "/api/profile", auth.AccessToken, new
        {
            displayName = "Only Display",
            carLicensePlate = (string?)null,
            preferredLocationId = (Guid?)null,
            preferredSlotId = (Guid?)null
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DataExport_ContainsNameAndCompany()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, displayName: "Erika Muster");

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/profile/data-export", auth.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"firstName\":\"Erika\"");
        body.Should().Contain("\"lastName\":\"Muster\"");
        body.Should().Contain($"\"company\":\"{AuthTestHelper.DefaultCompany}\"");
    }
}
