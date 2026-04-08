namespace SchulerPark.Api.DTOs.Admin;

public record AdminSlotDto(Guid Id, Guid LocationId, string SlotNumber, string? Label, bool IsActive);
