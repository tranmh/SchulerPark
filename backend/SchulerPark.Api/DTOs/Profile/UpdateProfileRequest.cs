namespace SchulerPark.Api.DTOs.Profile;

using System.ComponentModel.DataAnnotations;

public record UpdateProfileRequest(
    // Phase 21: DisplayName is derived ("First Last"); Company is not editable here.
    [Required, MinLength(1), MaxLength(100)] string FirstName,
    [Required, MinLength(1), MaxLength(100)] string LastName,
    [MaxLength(20)] string? CarLicensePlate,
    Guid? PreferredLocationId,
    Guid? PreferredSlotId,
    // Optional: "de" or "en". Omitted/null leaves the stored language unchanged.
    [MaxLength(10)] string? PreferredLanguage = null);
