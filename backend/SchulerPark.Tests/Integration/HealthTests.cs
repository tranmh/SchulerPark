using System.Net;
using FluentAssertions;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class HealthTests
{
    private readonly HttpClient _client;

    public HealthTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }
}
