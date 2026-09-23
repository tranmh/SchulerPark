namespace SchulerPark.Core.Settings;

/// <summary>Bound from the "Booking" configuration section (env <c>Booking__MaxDaysAhead</c>).</summary>
public class BookingSettings
{
    /// <summary>
    /// Last bookable day is Berlin today + this many days. The frontend reads the same
    /// value from <c>GET /api/bookings/window</c>, so client and server agree (WP3 3.1).
    /// </summary>
    public int MaxDaysAhead { get; set; } = 31;
}
