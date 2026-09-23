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
    string? FallbackReason,
    /// <summary>WP4 2.7: approximate 1-based waitlist position; only set for Waitlisted bookings.</summary>
    int? WaitlistPosition = null);
