namespace SchulerPark.Api.DTOs.Auth;

using System.ComponentModel.DataAnnotations;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(1), MaxLength(200)] string DisplayName,
    [Required, MinLength(8), MaxLength(128)] string Password);
