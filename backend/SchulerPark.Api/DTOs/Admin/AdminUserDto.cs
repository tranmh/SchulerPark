namespace SchulerPark.Api.DTOs.Admin;

public record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? Company,
    string Role,
    string ApprovalStatus,
    bool IsDisabled,
    bool HasAzureAd,
    DateTime CreatedAt);
