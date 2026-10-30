namespace SchulerPark.Api.DTOs.Admin;

public record PendingUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? Company,
    bool EmailVerified,
    DateTime CreatedAt);
