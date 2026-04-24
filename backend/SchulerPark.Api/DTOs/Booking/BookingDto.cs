namespace SchulerPark.Api.DTOs.Booking;

public record BookingDto(
    Guid Id,
    Guid LocationId,
    string LocationName,
    Guid? ParkingSlotId,
    string? ParkingSlotNumber,
    DateOnly Date,
    string TimeSlot,
    string Status,
    DateTime? ConfirmedAt,
    DateTime CreatedAt,
    DateTime? ConfirmationDeadline,
    string? FallbackReason);
