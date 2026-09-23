namespace SchulerPark.Tests.Helpers;

using FluentAssertions;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;

/// <summary>Phase 20 WP3 2.1: same-day cutoff = slot end in Europe/Berlin, DST-safe.</summary>
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

    [Fact]
    public void ConfirmationDeadline_IsSixAndOneBerlin_InUtc()
    {
        // CEST (UTC+2): 06:00 Berlin = 04:00 UTC, 13:00 Berlin = 11:00 UTC.
        DeadlineHelper.GetConfirmationDeadline(Day, TimeSlot.Morning).Should().Be(new DateTime(2026, 6, 10, 4, 0, 0));
        DeadlineHelper.GetConfirmationDeadline(Day, TimeSlot.Afternoon).Should().Be(new DateTime(2026, 6, 10, 11, 0, 0));
        DeadlineHelper.IsDeadlinePassed(Day, TimeSlot.Morning, new DateTime(2026, 6, 10, 3, 59, 59)).Should().BeFalse();
        DeadlineHelper.IsDeadlinePassed(Day, TimeSlot.Morning, new DateTime(2026, 6, 10, 4, 0, 0)).Should().BeTrue();
    }

    [Fact]
    public void BerlinToday_RollsOverAtBerlinMidnight()
    {
        // 22:30 UTC on the 10th is already 00:30 on the 11th in Berlin (CEST).
        DeadlineHelper.BerlinToday(new DateTime(2026, 6, 10, 22, 30, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 6, 11));
        DeadlineHelper.BerlinToday(new DateTime(2026, 6, 10, 21, 30, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 6, 10));
    }
}
