namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

public interface IWaitlistService
{
    /// <summary>
    /// Hands a freed slot to the best Waitlisted booking for the same location, date and time
    /// slot (WP4): as Won with a stored deadline while there is time to confirm, otherwise
    /// directly as Confirmed. No-op once the slot's day-part has ended.
    /// </summary>
    Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId);

    /// <summary>
    /// WP4 2.7: approximate 1-based waitlist position of each Waitlisted booking passed in,
    /// using the promotion ordering minus the preferred-slot term (which depends on the slot
    /// that frees up). Bookings that are not Waitlisted are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetWaitlistPositionsAsync(IReadOnlyCollection<Booking> bookings);
}
