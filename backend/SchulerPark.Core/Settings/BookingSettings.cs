namespace SchulerPark.Core.Settings;

/// <summary>Bound from the "Booking" configuration section (env <c>Booking__*</c>).</summary>
public class BookingSettings
{
    /// <summary>
    /// Last bookable day is Berlin today + this many days. The frontend reads the same
    /// value from <c>GET /api/bookings/window</c>, so client and server agree (WP3 3.1).
    /// </summary>
    public int MaxDaysAhead { get; set; } = 31;

    /// <summary>
    /// Berlin wall-clock time ("HH:mm") at which the nightly lottery for the next day runs.
    /// Drives the Hangfire cron and the times shown in the UI and mails.
    /// </summary>
    public string LotteryTime { get; set; } = "21:00";

    /// <summary>
    /// WP4 2.5: default confirmation deadlines ("HH:mm" Berlin) on the booking day. A win
    /// that arrives late (waitlist promotion) gets at least
    /// <see cref="MinConfirmationWindowMinutes"/>, capped at the slot's end.
    /// </summary>
    public ConfirmationDeadlineSettings ConfirmationDeadline { get; set; } = new();

    /// <summary>WP4 2.4: minimum time a user gets to confirm a late win, in minutes.</summary>
    public int MinConfirmationWindowMinutes { get; set; } = 120;

    // ── typed views (invalid strings fall back to the defaults) ──

    public TimeOnly LotteryTimeOfDay => ParseTime(LotteryTime, new TimeOnly(21, 0));
    public TimeOnly MorningDeadline => ParseTime(ConfirmationDeadline.Morning, new TimeOnly(7, 0));
    public TimeOnly AfternoonDeadline => ParseTime(ConfirmationDeadline.Afternoon, new TimeOnly(13, 0));
    public TimeSpan MinConfirmationWindow => TimeSpan.FromMinutes(Math.Max(0, MinConfirmationWindowMinutes));

    public static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value?.Trim(), "HH:mm", null, System.Globalization.DateTimeStyles.None, out var t)
            ? t
            : fallback;
}

public class ConfirmationDeadlineSettings
{
    public string Morning { get; set; } = "07:00";
    public string Afternoon { get; set; } = "13:00";
}
