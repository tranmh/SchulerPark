namespace SchulerPark.Api.DTOs.Push;

using System.ComponentModel.DataAnnotations;

public record UnsubscribePushRequest([Required] string Endpoint);
