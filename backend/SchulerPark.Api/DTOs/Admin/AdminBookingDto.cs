namespace SchulerPark.Api.DTOs.Admin;

public record AdminBookingDto(
    Guid Id, Guid UserId, string UserEmail, string UserDisplayName,
    Guid LocationId, string LocationName,
    Guid? ParkingSlotId, string? ParkingSlotNumber,
    DateOnly Date, string TimeSlot, string Status,
    DateTime? ConfirmedAt, DateTime CreatedAt);
