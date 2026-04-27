namespace SchulerPark.Api.DTOs.Admin;

public record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsDisabled,
    bool HasAzureAd,
    DateTime CreatedAt);
