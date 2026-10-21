namespace SchulerPark.Api.DTOs.Admin;

public record AdminLocationDto(
    Guid Id, string Name, bool IsActive,
    string DefaultAlgorithm, int TotalSlots, int ActiveSlots);
