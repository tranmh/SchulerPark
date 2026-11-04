namespace SchulerPark.Api.DTOs.Push;

using System.ComponentModel.DataAnnotations;
using SchulerPark.Infrastructure.Services;

// MaxLengths mirror PushSubscriptionConfiguration so an oversize value fails as a
// 400 here instead of a 500 at the DB layer. The stored Endpoint is fetched
// server-side by the WebPush client, so PushEndpointPolicy bounds the SSRF surface:
// https only (every real push service — FCM, Mozilla, WNS, Apple — is https) and
// never a loopback, private or link-local host.
public record SubscribePushRequest(
    [Required, MaxLength(2048), Url] string Endpoint,
    [Required, MaxLength(256)] string P256dh,
    [Required, MaxLength(256)] string Auth) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            yield return new ValidationResult("Endpoint must be an absolute https URL.", [nameof(Endpoint)]);
        else if (!PushEndpointPolicy.IsAllowed(uri))
            yield return new ValidationResult("Endpoint must point to a public push service.", [nameof(Endpoint)]);
    }
}
