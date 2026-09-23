namespace SchulerPark.Core.Helpers;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Settings;

public static class DeadlineHelper
{
    public static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Wall-clock start of each time slot in Europe/Berlin (matches the UI ranges).</summary>
    public static TimeOnly SlotStart(TimeSlot slot) =>
        slot == TimeSlot.Morning ? new TimeOnly(6, 0) : new TimeOnly(12, 0);

    /// <summary>Wall-clock end of each time slot in Europe/Berlin (matches the UI ranges).</summary>
    public static TimeOnly SlotEnd(TimeSlot slot) =>
        slot == TimeSlot.Morning ? new TimeOnly(12, 0) : new TimeOnly(18, 0);

    /// <summary>The slot's end on <paramref name="date"/> as a UTC instant.</summary>
    public static DateTime SlotEndUtc(DateOnly date, TimeSlot slot) => BerlinToUtc(date, SlotEnd(slot));

    /// <summary>
    /// WP4 2.5: the configured default confirmation deadline (Morning 07:00 / Afternoon 13:00
    /// Berlin unless overridden) on the booking day, in UTC.
    /// </summary>
    public static DateTime DefaultDeadline(DateOnly date, TimeSlot slot, BookingSettings settings) =>
        BerlinToUtc(date, slot == TimeSlot.Morning ? settings.MorningDeadline : settings.AfternoonDeadline);

    /// <summary>
    /// WP4 2.4: the deadline a booking that becomes Won at <paramref name="utcNow"/> gets:
    /// the default deadline, but never less than <see cref="BookingSettings.MinConfirmationWindow"/>
    /// from now, and never later than the slot's end. A 21:00 lottery win therefore gets the
    /// plain default; a 05:30 waitlist promotion gets until 07:30 (Morning) instead of 07:00.
    /// </summary>
    public static DateTime ComputeDeadline(DateOnly date, TimeSlot slot, DateTime utcNow, BookingSettings settings)
    {
        var deadline = DefaultDeadline(date, slot, settings);
        var earliest = utcNow + settings.MinConfirmationWindow;
        if (earliest > deadline) deadline = earliest;
        var end = SlotEndUtc(date, slot);
        return deadline < end ? deadline : end;
    }

    /// <summary>
    /// WP4: whether a late promotion should skip the Won step and confirm outright — true once
    /// the default deadline is less than the minimum window away (or already past).
    /// </summary>
    public static bool IsInsideConfirmationWindow(DateOnly date, TimeSlot slot, DateTime utcNow, BookingSettings settings) =>
        utcNow >= DefaultDeadline(date, slot, settings) - settings.MinConfirmationWindow;

    /// <summary>
    /// Whether the booking's stored confirmation deadline has passed. A Won booking without a
    /// stored deadline (should not exist after the WP4 backfill) is treated as due at slot end.
    /// </summary>
    public static bool IsDeadlinePassed(Booking booking, DateTime utcNow) =>
        utcNow >= (booking.ConfirmationDeadline ?? SlotEndUtc(booking.Date, booking.TimeSlot));

    /// <summary>Today's date in Europe/Berlin for the given UTC instant.</summary>
    public static DateOnly BerlinToday(DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, BerlinTz));

    /// <summary>The given UTC instant as Berlin wall-clock time.</summary>
    public static DateTime ToBerlin(DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcNow, BerlinTz);

    /// <summary>
    /// Berlin wall-clock → UTC. A time that does not exist on a spring-forward day (02:00–03:00)
    /// is shifted forward an hour instead of throwing; an ambiguous fall-back time resolves to
    /// the first (summer-time) occurrence.
    /// </summary>
    public static DateTime BerlinToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (BerlinTz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, BerlinTz);
    }

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
