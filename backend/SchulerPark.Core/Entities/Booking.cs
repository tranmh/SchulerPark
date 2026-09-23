namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class Booking
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParkingSlotId { get; set; }
    public Guid LocationId { get; set; }
    public DateOnly Date { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Phase 20 (WP1): audit trail for cancellations that were not made by the booking's
    // owner through the normal cancel flow — admin cancels, capacity removal, account
    // disable/deletion. All three stay null for a plain user cancel.
    /// <summary>Admin who cancelled the booking, or null when the system/owner did.</summary>
    public Guid? CancelledByUserId { get; set; }
    /// <summary>Free-text or machine reason (e.g. the BlockedDay reason, "user_disabled").</summary>
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public ParkingSlot? ParkingSlot { get; set; }
    public Location Location { get; set; } = null!;
    public User? CancelledByUser { get; set; }
}
