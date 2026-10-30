namespace SchulerPark.Api.DTOs.Auth;

using System.ComponentModel.DataAnnotations;
using SchulerPark.Api.Security;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(1), MaxLength(100)] string FirstName,
    [Required, MinLength(1), MaxLength(100)] string LastName,
    // Phase 21: employer, shown to admins when they approve an external account.
    [Required, MinLength(1), MaxLength(200)] string Company,
    [Required, MinLength(8), MaxLength(128), PasswordComplexity] string Password,
    // UI language at registration ("de"/"en"); anything else falls back to German.
    [MaxLength(10)] string? PreferredLanguage = null);
