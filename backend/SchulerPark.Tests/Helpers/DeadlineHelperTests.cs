namespace SchulerPark.Tests.Helpers;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Settings;

/// <summary>
/// Phase 20 WP3 2.1: same-day cutoff = slot end in Europe/Berlin, DST-safe.
/// Phase 20 WP4: configurable, stored confirmation deadlines with a minimum window.
/// </summary>
public class DeadlineHelperTests
{
    private static readonly DateOnly Day = new(2026, 6, 10);

    [Theory]
    [InlineData(11, 59, true)]
    [InlineData(12, 0, false)]
    [InlineData(12, 1, false)]
    public void Morning_IsOpenUntilNoon(int hour, int minute, bool expected)
    {
        var berlinNow = Day.ToDateTime(new TimeOnly(hour, minute));
        DeadlineHelper.IsSameDayBookingOpen(Day, TimeSlot.Morning, berlinNow).Should().Be(expected);
    }

    [Theory]
    [InlineData(17, 59, true)]
    [InlineData(18, 0, false)]
    [InlineData(23, 30, false)]
    public void Afternoon_IsOpenUntilSix(int hour, int minute, bool expected)
    {
        var berlinNow = Day.ToDateTime(new TimeOnly(hour, minute));
        DeadlineHelper.IsSameDayBookingOpen(Day, TimeSlot.Afternoon, berlinNow).Should().Be(expected);
    }

    [Fact]
    public void FutureDates_AlwaysOpen_PastDates_NeverOpen()
    {
        var berlinNow = Day.ToDateTime(new TimeOnly(23, 59));
        DeadlineHelper.IsSameDayBookingOpen(Day.AddDays(1), TimeSlot.Morning, berlinNow).Should().BeTrue();
        DeadlineHelper.IsSameDayBookingOpen(Day.AddDays(-1), TimeSlot.Afternoon, new DateTime(2026, 6, 10, 0, 0, 1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(2026, 3, 29, 9, 59, 30, true)]    // spring-forward day: 09:59 UTC = 11:59 CEST
    [InlineData(2026, 3, 29, 10, 0, 0, false)]    // 10:00 UTC = 12:00 CEST → closed
    [InlineData(2026, 10, 25, 10, 59, 0, true)]   // fall-back day: 10:59 UTC = 11:59 CET
    [InlineData(2026, 10, 25, 11, 0, 0, false)]   // 11:00 UTC = 12:00 CET → closed
    public void Cutoff_FollowsBerlinWallClock_AcrossDst(int y, int m, int d, int utcHour, int utcMinute, int utcSecond, bool expected)
    {
        var utc = new DateTime(y, m, d, utcHour, utcMinute, utcSecond, DateTimeKind.Utc);
        var berlinNow = DeadlineHelper.ToBerlin(utc);
        DeadlineHelper.IsSameDayBookingOpen(new DateOnly(y, m, d), TimeSlot.Morning, berlinNow).Should().Be(expected);
    }

    // ── WP4: stored, configurable deadlines ──────────────────────────────────────

    private static readonly BookingSettings Defaults = new();

    [Fact]
    public void DefaultDeadline_IsSevenAndThirteenBerlin_InUtc()
    {
        // CEST (UTC+2): 07:00 Berlin = 05:00 UTC, 13:00 Berlin = 11:00 UTC.
        DeadlineHelper.DefaultDeadline(Day, TimeSlot.Morning, Defaults).Should().Be(new DateTime(2026, 6, 10, 5, 0, 0));
        DeadlineHelper.DefaultDeadline(Day, TimeSlot.Afternoon, Defaults).Should().Be(new DateTime(2026, 6, 10, 11, 0, 0));
    }

    [Fact]
    public void DefaultDeadline_FollowsConfiguration()
    {
        var custom = new BookingSettings { ConfirmationDeadline = new ConfirmationDeadlineSettings { Morning = "06:30", Afternoon = "12:15" } };
        DeadlineHelper.DefaultDeadline(Day, TimeSlot.Morning, custom).Should().Be(new DateTime(2026, 6, 10, 4, 30, 0));
        DeadlineHelper.DefaultDeadline(Day, TimeSlot.Afternoon, custom).Should().Be(new DateTime(2026, 6, 10, 10, 15, 0));

        // Garbage falls back to the defaults instead of throwing at startup.
        var broken = new BookingSettings { ConfirmationDeadline = new ConfirmationDeadlineSettings { Morning = "7 o'clock" }, LotteryTime = "" };
        broken.MorningDeadline.Should().Be(new TimeOnly(7, 0));
        broken.LotteryTimeOfDay.Should().Be(new TimeOnly(21, 0));
    }

    [Fact]
    public void ComputeDeadline_LotteryWin_TheEveningBefore_GetsTheDefault()
    {
        // 21:00 Berlin the day before = 19:00 UTC.
        var lotteryRun = new DateTime(2026, 6, 9, 19, 0, 0, DateTimeKind.Utc);
        DeadlineHelper.ComputeDeadline(Day, TimeSlot.Morning, lotteryRun, Defaults).Should().Be(new DateTime(2026, 6, 10, 5, 0, 0));
        DeadlineHelper.ComputeDeadline(Day, TimeSlot.Afternoon, lotteryRun, Defaults).Should().Be(new DateTime(2026, 6, 10, 11, 0, 0));
    }

    [Fact]
    public void ComputeDeadline_LateWin_GetsAtLeastTheMinimumWindow()
    {
        // Promoted at 05:30 Berlin (03:30 UTC): 07:00 would leave 90 min → pushed to 07:30.
        var promotedAt = new DateTime(2026, 6, 10, 3, 30, 0, DateTimeKind.Utc);
        DeadlineHelper.ComputeDeadline(Day, TimeSlot.Morning, promotedAt, Defaults).Should().Be(new DateTime(2026, 6, 10, 5, 30, 0));
    }

    [Fact]
    public void ComputeDeadline_IsCappedAtSlotEnd()
    {
        // Promoted at 11:30 Berlin (09:30 UTC): +2 h would be 13:30, but Morning ends 12:00 (10:00 UTC).
        var promotedAt = new DateTime(2026, 6, 10, 9, 30, 0, DateTimeKind.Utc);
        DeadlineHelper.ComputeDeadline(Day, TimeSlot.Morning, promotedAt, Defaults).Should().Be(new DateTime(2026, 6, 10, 10, 0, 0));
    }

    [Theory]
    [InlineData(2026, 3, 29, 6, 0, 0)]    // spring-forward day: 07:00 CEST = 05:00 UTC
    [InlineData(2026, 10, 25, 7, 0, 0)]   // fall-back day: 07:00 CET = 06:00 UTC
    public void DefaultDeadline_FollowsBerlinWallClock_AcrossDst(int y, int m, int d, int expectedUtcHour, int expectedUtcMinute, int expectedUtcSecond)
    {
        var expected = new DateTime(y, m, d, expectedUtcHour, expectedUtcMinute, expectedUtcSecond);
        // Both DST days: 07:00 Berlin. On 29 March the offset is already +2 (05:00 UTC); on 25 October it is +1 (06:00 UTC).
        var utc = DeadlineHelper.DefaultDeadline(new DateOnly(y, m, d), TimeSlot.Morning, Defaults);
        utc.Should().Be(y == 2026 && m == 3 ? new DateTime(2026, 3, 29, 5, 0, 0) : new DateTime(2026, 10, 25, 6, 0, 0));
        expected.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void IsInsideConfirmationWindow_StartsTwoHoursBeforeTheDefault()
    {
        // Morning default 07:00 Berlin → window opens 05:00 Berlin = 03:00 UTC.
        DeadlineHelper.IsInsideConfirmationWindow(Day, TimeSlot.Morning, new DateTime(2026, 6, 10, 2, 59, 59), Defaults).Should().BeFalse();
        DeadlineHelper.IsInsideConfirmationWindow(Day, TimeSlot.Morning, new DateTime(2026, 6, 10, 3, 0, 0), Defaults).Should().BeTrue();
        DeadlineHelper.IsInsideConfirmationWindow(Day, TimeSlot.Morning, new DateTime(2026, 6, 10, 8, 0, 0), Defaults).Should().BeTrue();
    }

    [Fact]
    public void IsDeadlinePassed_ReadsTheStoredValue_AndFallsBackToSlotEnd()
    {
        var booking = new Booking { Date = Day, TimeSlot = TimeSlot.Morning, ConfirmationDeadline = new DateTime(2026, 6, 10, 6, 30, 0, DateTimeKind.Utc) };
        DeadlineHelper.IsDeadlinePassed(booking, new DateTime(2026, 6, 10, 6, 29, 59)).Should().BeFalse();
        DeadlineHelper.IsDeadlinePassed(booking, new DateTime(2026, 6, 10, 6, 30, 0)).Should().BeTrue();

        var legacy = new Booking { Date = Day, TimeSlot = TimeSlot.Morning, ConfirmationDeadline = null };
        DeadlineHelper.IsDeadlinePassed(legacy, new DateTime(2026, 6, 10, 9, 59, 0)).Should().BeFalse();   // before 12:00 Berlin
        DeadlineHelper.IsDeadlinePassed(legacy, new DateTime(2026, 6, 10, 10, 0, 0)).Should().BeTrue();
    }

    [Fact]
    public void BerlinToday_RollsOverAtBerlinMidnight()
    {
        // 22:30 UTC on the 10th is already 00:30 on the 11th in Berlin (CEST).
        DeadlineHelper.BerlinToday(new DateTime(2026, 6, 10, 22, 30, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 6, 11));
        DeadlineHelper.BerlinToday(new DateTime(2026, 6, 10, 21, 30, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 6, 10));
    }
}
