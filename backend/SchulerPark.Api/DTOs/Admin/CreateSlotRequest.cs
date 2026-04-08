namespace SchulerPark.Api.DTOs.Admin;

public record CreateSlotRequest(Guid LocationId, string SlotNumber, string? Label);
