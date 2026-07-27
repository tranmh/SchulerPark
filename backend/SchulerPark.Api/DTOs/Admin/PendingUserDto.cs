namespace SchulerPark.Api.DTOs.Admin;

public record PendingUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified,
    DateTime CreatedAt);
