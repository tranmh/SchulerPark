namespace SchulerPark.Core.Helpers;

using SchulerPark.Core.Enums;

public static class DeadlineHelper
{
    public static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Wall-clock start of each time slot in Europe/Berlin (matches the UI ranges).</summary>
    public static TimeOnly SlotStart(TimeSlot slot) =>
        slot == TimeSlot.Morning ? new TimeOnly(6, 0) : new TimeOnly(12, 0);

    /// <summary>Wall-clock end of each time slot in Europe/Berlin (matches the UI ranges).</summary>
    public static TimeOnly SlotEnd(TimeSlot slot) =>
        slot == TimeSlot.Morning ? new TimeOnly(12, 0) : new TimeOnly(18, 0);

    /// <summary>
    /// Returns the confirmation deadline in UTC.
    /// 1 hour before slot start: Morning 06:00, Afternoon 13:00 Europe/Berlin.
    /// </summary>
    public static DateTime GetConfirmationDeadline(DateOnly date, TimeSlot timeSlot)
    {
        var hour = timeSlot == TimeSlot.Morning ? 6 : 13;
        var berlinTime = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0);
        return TimeZoneInfo.ConvertTimeToUtc(berlinTime, BerlinTz);
    }

    public static bool IsDeadlinePassed(DateOnly date, TimeSlot timeSlot) =>
        IsDeadlinePassed(date, timeSlot, DateTime.UtcNow);

    /// <summary>Time-injectable variant (Phase 20): callers with a <see cref="TimeProvider"/> pass its UTC now.</summary>
    public static bool IsDeadlinePassed(DateOnly date, TimeSlot timeSlot, DateTime utcNow) =>
        utcNow >= GetConfirmationDeadline(date, timeSlot);

    /// <summary>Today's date in Europe/Berlin for the given UTC instant.</summary>
    public static DateOnly BerlinToday(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, BerlinTz));

    /// <summary>The given UTC instant as Berlin wall-clock time.</summary>
    public static DateTime ToBerlin(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcNow, BerlinTz);

    /// <summary>
    /// Same-day booking cutoff (Phase 17 Part B / Phase 20 WP3 2.1): a slot on <paramref name="date"/>
    /// can still be booked while the Berlin wall clock is before the slot's end (Morning until
    /// 12:00, Afternoon until 18:00). Future dates are always open, past dates never.
    /// </summary>
    public static bool IsSameDayBookingOpen(DateOnly date, TimeSlot timeSlot, DateTime berlinNow)
    {
        var today = DateOnly.FromDateTime(berlinNow);
        if (date > today) return true;
        if (date < today) return false;
        return TimeOnly.FromDateTime(berlinNow) < SlotEnd(timeSlot);
    }
}
