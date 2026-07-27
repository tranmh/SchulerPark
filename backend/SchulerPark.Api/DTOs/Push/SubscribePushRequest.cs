namespace SchulerPark.Api.DTOs.Push;

using System.ComponentModel.DataAnnotations;

// MaxLengths mirror PushSubscriptionConfiguration so an oversize value fails as a
// 400 here instead of a 500 at the DB layer. The https requirement bounds the SSRF
// surface: the stored Endpoint is fetched server-side by the WebPush client, and
// every real push service (FCM, Mozilla, WNS, Apple) is https.
public record SubscribePushRequest(
    [Required, MaxLength(2048), Url] string Endpoint,
    [Required, MaxLength(256)] string P256dh,
    [Required, MaxLength(256)] string Auth) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            yield return new ValidationResult("Endpoint must be an absolute https URL.", [nameof(Endpoint)]);
    }
}
