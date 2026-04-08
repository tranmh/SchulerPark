namespace SchulerPark.Api.DTOs.Admin;

public record CreateBlockedDayRequest(Guid LocationId, Guid? ParkingSlotId, DateOnly Date, string? Reason);
