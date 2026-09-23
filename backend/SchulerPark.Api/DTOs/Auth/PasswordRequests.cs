namespace SchulerPark.Api.DTOs.Auth;

using System.ComponentModel.DataAnnotations;
using SchulerPark.Core.Helpers;

// Phase 20 WP2. Complexity is checked in AuthService (coded `password_too_weak`) so the
// client gets a localizable ProblemDetails code; the attributes only bound the payload.

public record ForgotPasswordRequest(
    [Required, EmailAddress, MaxLength(320)] string Email);

public record ResetPasswordRequest(
    [Required, MaxLength(200)] string Token,
    [Required, MaxLength(PasswordPolicy.MaxLength)] string NewPassword);

public record ChangePasswordRequest(
    [Required, MaxLength(PasswordPolicy.MaxLength)] string CurrentPassword,
    [Required, MaxLength(PasswordPolicy.MaxLength)] string NewPassword);
