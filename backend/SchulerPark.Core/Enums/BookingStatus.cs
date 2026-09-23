namespace SchulerPark.Core.Enums;

public enum BookingStatus
{
    Pending = 0,
    Won = 1,
    /// <summary>Terminal (Phase 20 WP4): the slot's day is over and no space was ever found.</summary>
    Lost = 2,
    Confirmed = 3,
    Cancelled = 4,
    Expired = 5,
    /// <summary>
    /// Phase 20 WP4: no slot yet, but the day is still ahead — promoted automatically when
    /// a slot frees up (see <c>WaitlistService</c>). Replaces the old use of <see cref="Lost"/>
    /// for lottery losers and full-day bookings.
    /// </summary>
    Waitlisted = 6
}
