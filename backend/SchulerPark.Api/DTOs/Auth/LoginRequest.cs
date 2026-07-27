namespace SchulerPark.Api.DTOs.Auth;

using System.ComponentModel.DataAnnotations;

public record LoginRequest(
    [Required, MaxLength(320)] string Email,
    [Required, MaxLength(128)] string Password);
