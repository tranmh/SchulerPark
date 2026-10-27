namespace SchulerPark.Tests.DTOs;

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using SchulerPark.Api.DTOs.Push;

public class SubscribePushRequestTests
{
    private static bool IsValid(string endpoint)
    {
        var request = new SubscribePushRequest(endpoint, "p256dh-key", "auth-secret");
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
    }

    [Fact]
    public void AcceptsPublicPushServiceEndpoint()
    {
        IsValid("https://fcm.googleapis.com/fcm/send/abc123").Should().BeTrue();
    }

    // The endpoint is POSTed to server-side, so it must never point into the
    // server's own network (loopback, link-local metadata, private ranges).
    [Theory]
    [InlineData("https://127.0.0.1:8443/")]
    [InlineData("https://localhost/x")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://192.168.1.10/")]
    [InlineData("https://172.16.0.1/")]
    [InlineData("https://[::1]/")]
    public void RejectsInternalNetworkEndpoints(string endpoint)
    {
        IsValid(endpoint).Should().BeFalse();
    }
}
