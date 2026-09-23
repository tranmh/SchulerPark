namespace SchulerPark.Core.Models;

/// <summary>
/// What <c>IBookingLifecycleService</c> did to existing bookings when capacity or a user
/// went away (WP1 3.3/3.4). Returned to admins so the UI can toast the impact.
/// </summary>
/// <param name="Affected">Bookings that were touched at all.</param>
/// <param name="Reassigned">Won/Confirmed bookings moved to another free slot (status kept).</param>
/// <param name="Waitlisted">Won/Confirmed bookings that lost their slot and went to the waitlist.</param>
/// <param name="Cancelled">Bookings cancelled outright (whole-location removal, user gone).</param>
public record CapacityChangeResult(int Affected, int Reassigned, int Waitlisted, int Cancelled)
{
    public static readonly CapacityChangeResult Empty = new(0, 0, 0, 0);

    public CapacityChangeResult Add(CapacityChangeResult other) => new(
        Affected + other.Affected,
        Reassigned + other.Reassigned,
        Waitlisted + other.Waitlisted,
        Cancelled + other.Cancelled);
}
