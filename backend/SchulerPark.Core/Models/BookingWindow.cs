namespace SchulerPark.Core.Models;

/// <summary>
/// The bookable date window, computed on the server in Europe/Berlin (WP3 3.1). The
/// frontend reads it from <c>GET /api/bookings/window</c> so both sides agree.
/// </summary>
/// <param name="Today">Berlin calendar date right now.</param>
/// <param name="MinDate">Earliest bookable day: today while at least one same-day slot is still open, else tomorrow.</param>
/// <param name="MaxDate">Latest bookable day: today + <see cref="MaxDaysAhead"/>.</param>
/// <param name="MaxDaysAhead">Configured horizon (<c>Booking:MaxDaysAhead</c>).</param>
/// <param name="MorningOpenToday">Whether today's Morning slot can still be booked (before 12:00 Berlin).</param>
/// <param name="AfternoonOpenToday">Whether today's Afternoon slot can still be booked (before 18:00 Berlin).</param>
public record BookingWindow(
    DateOnly Today,
    DateOnly MinDate,
    DateOnly MaxDate,
    int MaxDaysAhead,
    bool MorningOpenToday,
    bool AfternoonOpenToday);
