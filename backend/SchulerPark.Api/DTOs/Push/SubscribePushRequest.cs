namespace SchulerPark.Api.DTOs.Push;

public record SubscribePushRequest(string Endpoint, string P256dh, string Auth);
