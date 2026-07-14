namespace SchulerPark.Api.DTOs.Auth;

using System.ComponentModel.DataAnnotations;

public record VerifyEmailRequest(
    [Required, MaxLength(128)] string Token);

public record ResendVerificationRequest(
    [Required, EmailAddress, MaxLength(320)] string Email);
