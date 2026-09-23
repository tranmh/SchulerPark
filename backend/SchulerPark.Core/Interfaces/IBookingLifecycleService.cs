namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Models;

/// <summary>Why bookings are being released or re-evaluated (WP1). Stored in <c>Booking.CancelReason</c> when a booking is cancelled.</summary>
public enum BookingReleaseReason
{
    UserDisabled,
    UserDeleted,
    AdminCancelled,
    SlotBlocked,
    SlotDeactivated,
    LocationBlocked,
    LocationDeactivated
}

/// <summary>
/// The single place that turns "capacity or user went away" into booking state.
/// Both <c>BookingService</c> and the admin controllers call it (WP1 3.3/3.4).
/// </summary>
public interface IBookingLifecycleService
{
    /// <summary>
    /// 3.3 — cancels every Pending/Won/Confirmed/Lost booking of the user dated Berlin
    /// today or later, frees the slots and promotes waitlisters. The user gets no
    /// notification (the account is gone); waitlisters get the usual promotion mail.
    /// </summary>
    Task<CapacityChangeResult> ReleaseUserBookingsAsync(Guid userId, BookingReleaseReason reason);

    /// <summary>
    /// 3.4 — re-evaluates bookings at the location for the date range after capacity was
    /// removed. Slot-level removal (<paramref name="parkingSlotId"/> set): Won/Confirmed on
    /// that slot are moved to a free slot or waitlisted. Whole-location removal
    /// (<paramref name="parkingSlotId"/> null): every live booking is cancelled with the
    /// admin's reason. <paramref name="to"/> null means every future date.
    /// </summary>
    Task<CapacityChangeResult> HandleCapacityRemovedAsync(
        Guid locationId, DateOnly from, DateOnly? to, Guid? parkingSlotId,
        BookingReleaseReason reason, Guid actedByUserId, string? adminReason);
}
