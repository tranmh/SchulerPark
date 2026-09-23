namespace SchulerPark.Api.DTOs.Admin;

public record AdminBookingDto(
    Guid Id, Guid UserId, string UserEmail, string UserDisplayName,
    Guid LocationId, string LocationName,
    Guid? ParkingSlotId, string? ParkingSlotNumber,
    DateOnly Date, string TimeSlot, string Status,
    DateTime? ConfirmedAt, DateTime CreatedAt,
    // Phase 20 WP1 cancellation audit (null unless cancelled by an admin/the system)
    DateTime? CancelledAt = null, string? CancelReason = null, Guid? CancelledByUserId = null,
    // Phase 20 WP4 follow-up: why the system confirmed it (null = the user did)
    string? AutoConfirmReason = null);
