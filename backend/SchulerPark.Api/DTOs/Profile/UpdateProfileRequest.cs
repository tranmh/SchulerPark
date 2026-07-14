namespace SchulerPark.Api.DTOs.Profile;

using System.ComponentModel.DataAnnotations;

public record UpdateProfileRequest(
    [Required, MinLength(1), MaxLength(200)] string DisplayName,
    [MaxLength(20)] string? CarLicensePlate,
    Guid? PreferredLocationId,
    Guid? PreferredSlotId);
