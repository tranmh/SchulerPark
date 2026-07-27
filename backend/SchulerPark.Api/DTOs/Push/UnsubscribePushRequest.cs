namespace SchulerPark.Api.DTOs.Push;

using System.ComponentModel.DataAnnotations;

public record UnsubscribePushRequest([Required, MaxLength(2048)] string Endpoint);
