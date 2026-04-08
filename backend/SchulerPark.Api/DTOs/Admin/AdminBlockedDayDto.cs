namespace SchulerPark.Api.DTOs.Admin;

public record AdminBlockedDayDto(
    Guid Id, Guid LocationId, string LocationName,
    Guid? ParkingSlotId, string? SlotNumber,
    DateOnly Date, string? Reason, DateTime CreatedAt);
