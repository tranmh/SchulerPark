namespace SchulerPark.Api.DTOs.Location;

public record BlockedDayDto(Guid Id, DateOnly Date, Guid? ParkingSlotId, string? Reason);
