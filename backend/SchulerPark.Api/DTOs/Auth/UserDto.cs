namespace SchulerPark.Api.DTOs.Auth;

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? CarLicensePlate,
    string Role,
    bool HasAzureAd,
    Guid? PreferredLocationId,
    Guid? PreferredSlotId);
