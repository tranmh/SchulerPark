namespace SchulerPark.Api.DTOs.Auth;

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? CarLicensePlate,
    string Role,
    bool HasAzureAd,
    Guid? PreferredLocationId,
    Guid? PreferredSlotId,
    string PreferredLanguage,
    /// <summary>True when the account has a local password (change-password is offered); false for SSO-only accounts.</summary>
    bool HasPassword)
{
    public static UserDto From(Core.Entities.User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.CarLicensePlate,
        user.Role.ToString(),
        !string.IsNullOrEmpty(user.AzureAdObjectId),
        user.PreferredLocationId,
        user.PreferredSlotId,
        user.PreferredLanguage,
        !string.IsNullOrEmpty(user.PasswordHash));
}
