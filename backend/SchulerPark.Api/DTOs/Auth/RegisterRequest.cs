namespace SchulerPark.Api.DTOs.Auth;

using System.ComponentModel.DataAnnotations;
using SchulerPark.Api.Security;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(1), MaxLength(200)] string DisplayName,
    [Required, MinLength(8), MaxLength(128), PasswordComplexity] string Password,
    // UI language at registration ("de"/"en"); anything else falls back to German.
    [MaxLength(10)] string? PreferredLanguage = null);
