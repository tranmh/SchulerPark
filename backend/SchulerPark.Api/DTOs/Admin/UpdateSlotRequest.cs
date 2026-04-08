namespace SchulerPark.Api.DTOs.Admin;

public record UpdateSlotRequest(string SlotNumber, string? Label, bool IsActive);
