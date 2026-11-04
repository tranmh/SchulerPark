namespace SchulerPark.Tests.Services;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Tests.Integration;
using SchulerPark.Tests.TestSupport;

/// <summary>Direct assignment stand-in: applies a fixed outcome and records how it was asked.</summary>
public sealed class ScriptedDirectAssignment : IDirectAssignmentService
{
    public DirectAssignmentOutcome Outcome { get; set; } = DirectAssignmentOutcome.NotApplicable;
    public Guid? SlotId { get; set; }
    public List<(Guid BookingId, bool AssumeLotteryRan)> Calls { get; } = [];

    public Task<DirectAssignmentOutcome> ApplyAsync(Booking booking, bool assumeLotteryRan = false)
    {
        Calls.Add((booking.Id, assumeLotteryRan));
        switch (Outcome)
        {
            case DirectAssignmentOutcome.AssignedConfirmed:
                booking.Status = BookingStatus.Confirmed;
                booking.ParkingSlotId = SlotId;
                break;
            case DirectAssignmentOutcome.Waitlisted:
                booking.Status = BookingStatus.Waitlisted;
                break;
        }
        return Task.FromResult(Outcome);
    }
}

/// <summary>Waitlist stand-in that records every promotion request.</summary>
public sealed class RecordingWaitlistService : IWaitlistService
{
    public List<(Guid LocationId, DateOnly Date, TimeSlot TimeSlot, Guid FreedSlotId)> Promotions { get; } = [];

    public Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId)
    {
        Promotions.Add((locationId, date, timeSlot, freedSlotId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetWaitlistPositionsAsync(IReadOnlyCollection<Booking> bookings)
        => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());
}

public class BookingServiceTests
{
    // FixedTimeProvider.DefaultNow: Monday 2026-03-02 10:00 UTC = 11:00 Berlin (CET).
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly FixedTimeProvider _time = new();
    private readonly ScriptedDirectAssignment _direct = new();
    private readonly RecordingWaitlistService _waitlist = new();
    private readonly CapturingEmailService _email = new();
    private readonly RecordingPushService _push = new();

    private BookingService NewService(int maxDaysAhead = 31) => new(
        _db, _waitlist, _direct, _email, _push,
        TestSettings.Booking(s => s.MaxDaysAhead = maxDaysAhead), _time);

    private static async Task<ValidationException> ExpectValidation(Func<Task> act, string code)
    {
        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Code.Should().Be(code);
        return ex;
    }

    // ---- create: date window -------------------------------------------------------

    [Fact]
    public async Task CreateBookingAsync_FutureDate_CreatesPendingBookingWithLocationLoaded()
    {
        var (user, location, _) = TestData.SeedSite(_db);

        var (booking, fallback) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning);

        fallback.Should().BeNull();
        booking.Status.Should().Be(BookingStatus.Pending);
        booking.UserId.Should().Be(user.Id);
        booking.LocationId.Should().Be(location.Id);
        booking.CreatedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        booking.Location.Should().NotBeNull();
        _db.Bookings.Should().ContainSingle(b => b.Id == booking.Id);
        _direct.Calls.Should().ContainSingle().Which.AssumeLotteryRan.Should().BeFalse();
        _email.Sent.Should().BeEmpty();
        _push.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBookingAsync_PastDate_ThrowsBookingDateInPast()
    {
        var (user, location, _) = TestData.SeedSite(_db);

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(-1), TimeSlot.Morning),
            "booking_date_in_past");
        _db.Bookings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBookingAsync_UsesBerlinDate_NotUtcDate()
    {
        // 23:30 UTC on Mar 1 is already 00:30 on Mar 2 in Berlin: Mar 1 is the past.
        _time.Set(new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero));
        var (user, location, _) = TestData.SeedSite(_db);

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, new DateOnly(2026, 3, 1), TimeSlot.Afternoon),
            "booking_date_in_past");
        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Morning);
        booking.Date.Should().Be(Today);
    }

    [Fact]
    public async Task CreateBookingAsync_LastDayOfWindow_IsAccepted()
    {
        var (user, location, _) = TestData.SeedSite(_db);

        var (booking, _) = await NewService(maxDaysAhead: 31).CreateBookingAsync(user.Id, location.Id, Today.AddDays(31), TimeSlot.Afternoon);

        booking.Date.Should().Be(Today.AddDays(31));
    }

    [Fact]
    public async Task CreateBookingAsync_OneDayBeyondWindow_ThrowsTooFarAhead()
    {
        var (user, location, _) = TestData.SeedSite(_db);

        var ex = await ExpectValidation(() => NewService(maxDaysAhead: 31).CreateBookingAsync(user.Id, location.Id, Today.AddDays(32), TimeSlot.Morning),
            "booking_too_far_ahead");
        ex.Message.Should().Contain("31");
    }

    [Fact]
    public async Task CreateBookingAsync_ConfiguredMaxDaysAhead_IsHonoured()
    {
        var (user, location, _) = TestData.SeedSite(_db);

        await ExpectValidation(() => NewService(maxDaysAhead: 7).CreateBookingAsync(user.Id, location.Id, Today.AddDays(8), TimeSlot.Morning),
            "booking_too_far_ahead");
        var (booking, _) = await NewService(maxDaysAhead: 7).CreateBookingAsync(user.Id, location.Id, Today.AddDays(7), TimeSlot.Morning);
        booking.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateBookingAsync_SameDayBeforeSlotEnd_AssignsDirectlyAssumingLotteryRan()
    {
        var (user, location, slots) = TestData.SeedSite(_db);
        _direct.Outcome = DirectAssignmentOutcome.AssignedConfirmed;
        _direct.SlotId = slots[0].Id;

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Morning);

        _direct.Calls.Should().ContainSingle().Which.AssumeLotteryRan.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ParkingSlot.Should().NotBeNull();
        booking.ParkingSlot!.Id.Should().Be(slots[0].Id);
        _email.Sent.Should().ContainSingle().Which.Should().Be(("DirectlyConfirmed", booking.Id));
        _push.Sent.Should().ContainSingle().Which.Should().Be(("DirectlyConfirmed", booking.Id));
    }

    [Fact]
    public async Task CreateBookingAsync_SameDayAtMorningEnd_ThrowsSameDayClosed()
    {
        _time.Set(new DateTimeOffset(2026, 3, 2, 11, 0, 0, TimeSpan.Zero)); // 12:00 Berlin
        var (user, location, _) = TestData.SeedSite(_db);

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Morning),
            "booking_same_day_closed");
    }

    [Fact]
    public async Task CreateBookingAsync_SameDayOneSecondBeforeMorningEnd_IsAccepted()
    {
        _time.Set(new DateTimeOffset(2026, 3, 2, 10, 59, 59, TimeSpan.Zero)); // 11:59:59 Berlin
        var (user, location, _) = TestData.SeedSite(_db);

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Morning);

        booking.Date.Should().Be(Today);
    }

    [Fact]
    public async Task CreateBookingAsync_SameDayAfterMorningEnd_AfternoonStillOpen()
    {
        _time.Set(new DateTimeOffset(2026, 3, 2, 11, 30, 0, TimeSpan.Zero)); // 12:30 Berlin
        var (user, location, _) = TestData.SeedSite(_db);

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Afternoon);

        booking.TimeSlot.Should().Be(TimeSlot.Afternoon);
    }

    [Fact]
    public async Task CreateBookingAsync_SameDayAtAfternoonEnd_ThrowsSameDayClosed()
    {
        _time.Set(new DateTimeOffset(2026, 3, 2, 18, 0, 0, TimeSpan.Zero)); // 19:00 Berlin
        var (user, location, _) = TestData.SeedSite(_db);

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Afternoon),
            "booking_same_day_closed");
    }

    [Fact]
    public async Task CreateBookingAsync_FullDay_BecomesWaitlistedAndNotifies()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        _direct.Outcome = DirectAssignmentOutcome.Waitlisted;

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today, TimeSlot.Afternoon);

        booking.Status.Should().Be(BookingStatus.Waitlisted);
        booking.ParkingSlotId.Should().BeNull();
        _email.Sent.Should().ContainSingle().Which.Should().Be(("Waitlisted", booking.Id));
        _push.Sent.Should().ContainSingle().Which.Should().Be(("Waitlisted", booking.Id));
    }

    // ---- create: limits and location checks --------------------------------------

    [Fact]
    public async Task CreateBookingAsync_FiftyBookingsCreatedInLastDay_ThrowsDailyLimit()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var recent = _time.GetUtcNow().UtcDateTime.AddHours(-1);
        for (var i = 0; i < 50; i++)
            _db.Bookings.Add(TestData.Booking(user, location, Today.AddDays(1 + i % 20), status: BookingStatus.Cancelled,
                configure: b => b.CreatedAt = recent));
        _db.SaveChanges();

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(25), TimeSlot.Morning),
            "booking_daily_limit");
    }

    [Fact]
    public async Task CreateBookingAsync_FortyNineBookingsInLastDay_StillAllowed()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var recent = _time.GetUtcNow().UtcDateTime.AddHours(-1);
        var old = _time.GetUtcNow().UtcDateTime.AddDays(-2);
        for (var i = 0; i < 49; i++)
            _db.Bookings.Add(TestData.Booking(user, location, Today.AddDays(1 + i % 20), status: BookingStatus.Cancelled,
                configure: b => b.CreatedAt = recent));
        for (var i = 0; i < 10; i++)
            _db.Bookings.Add(TestData.Booking(user, location, Today.AddDays(1 + i), status: BookingStatus.Cancelled,
                configure: b => b.CreatedAt = old));
        _db.SaveChanges();

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(25), TimeSlot.Morning);

        booking.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateBookingAsync_InactiveLocation_ThrowsNotFound()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        location.IsActive = false;
        _db.SaveChanges();

        await NewService().Invoking(s => s.CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateBookingAsync_UnknownLocation_ThrowsNotFound()
    {
        var (user, _, _) = TestData.SeedSite(_db);

        await NewService().Invoking(s => s.CreateBookingAsync(user.Id, Guid.NewGuid(), Today.AddDays(1), TimeSlot.Morning))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateBookingAsync_LocationWithoutActiveSlots_ThrowsNoActiveSlots()
    {
        var (user, location, slots) = TestData.SeedSite(_db, slotCount: 2);
        slots.ForEach(s => s.IsActive = false);
        _db.SaveChanges();
        // A tracked Location keeps every slot in its collection; read it fresh like a new request would.
        _db.ChangeTracker.Clear();

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning),
            "location_no_active_slots");
    }

    [Fact]
    public async Task CreateBookingAsync_LocationBlockedOnDate_ThrowsLocationBlocked()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        _db.BlockedDays.Add(TestData.BlockedDay(location, Today.AddDays(1), user));
        _db.SaveChanges();

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning),
            "location_blocked");
        // Another date is unaffected.
        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(2), TimeSlot.Morning);
        booking.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateBookingAsync_EverySlotBlocked_ThrowsSlotsAllBlocked()
    {
        var (user, location, slots) = TestData.SeedSite(_db, slotCount: 2);
        foreach (var slot in slots)
            _db.BlockedDays.Add(TestData.BlockedDay(location, Today.AddDays(1), user, slot));
        _db.SaveChanges();

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning),
            "slots_all_blocked");
    }

    [Fact]
    public async Task CreateBookingAsync_SomeSlotsBlocked_IsAccepted()
    {
        var (user, location, slots) = TestData.SeedSite(_db, slotCount: 2);
        _db.BlockedDays.Add(TestData.BlockedDay(location, Today.AddDays(1), user, slots[0]));
        _db.SaveChanges();

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning);

        booking.Should().NotBeNull();
    }

    // ---- create: duplicates --------------------------------------------------------

    [Fact]
    public async Task CreateBookingAsync_LiveBookingSameLocation_ThrowsDuplicate()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        _db.Bookings.Add(TestData.Booking(user, location, Today.AddDays(1), TimeSlot.Morning, BookingStatus.Waitlisted));
        _db.SaveChanges();

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning),
            "booking_duplicate");
    }

    [Fact]
    public async Task CreateBookingAsync_LiveBookingOtherLocation_ThrowsDuplicateOtherLocationWithName()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var other = TestData.Location(l => l.Name = "Weingarten");
        _db.Locations.Add(other);
        _db.ParkingSlots.Add(TestData.Slot(other, "W-1"));
        _db.Bookings.Add(TestData.Booking(user, other, Today.AddDays(1), TimeSlot.Morning, BookingStatus.Confirmed));
        _db.SaveChanges();

        var ex = await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning),
            "booking_duplicate_other_location");

        ex.Parameters.Should().NotBeNull();
        ex.Parameters!["location"].Should().Be("Weingarten");
        ex.Message.Should().Contain("Weingarten");
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.Lost)]
    public async Task CreateBookingAsync_DeadBookingSameSlot_DoesNotCollide(BookingStatus status)
    {
        var (user, location, _) = TestData.SeedSite(_db);
        _db.Bookings.Add(TestData.Booking(user, location, Today.AddDays(1), TimeSlot.Morning, status));
        _db.SaveChanges();

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning);

        booking.Status.Should().Be(BookingStatus.Pending);
    }

    [Fact]
    public async Task CreateBookingAsync_OtherTimeSlotSameDay_DoesNotCollide()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        _db.Bookings.Add(TestData.Booking(user, location, Today.AddDays(1), TimeSlot.Morning, BookingStatus.Pending));
        _db.SaveChanges();

        var (booking, _) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Afternoon);

        booking.TimeSlot.Should().Be(TimeSlot.Afternoon);
    }

    // ---- create: preferred-location fallback -------------------------------------

    [Fact]
    public async Task CreateBookingAsync_NoLocationAndNoPreferred_ThrowsNoPreferredLocation()
    {
        var (user, _, _) = TestData.SeedSite(_db);

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, null, Today.AddDays(1), TimeSlot.Morning),
            "no_preferred_location");
    }

    [Fact]
    public async Task CreateBookingAsync_NoLocationUnknownUser_ThrowsNotFound()
    {
        TestData.SeedSite(_db);

        await NewService().Invoking(s => s.CreateBookingAsync(Guid.NewGuid(), null, Today.AddDays(1), TimeSlot.Morning))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateBookingAsync_NoLocation_UsesAvailablePreferredLocation()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        user.PreferredLocationId = location.Id;
        _db.SaveChanges();

        var (booking, fallback) = await NewService().CreateBookingAsync(user.Id, null, Today.AddDays(1), TimeSlot.Morning);

        booking.LocationId.Should().Be(location.Id);
        fallback.Should().BeNull();
    }

    [Fact]
    public async Task CreateBookingAsync_PreferredBlocked_FallsBackToOtherLocationWithReason()
    {
        var (user, preferred, _) = TestData.SeedSite(_db);
        preferred.Name = "Erfurt";
        user.PreferredLocationId = preferred.Id;
        var alternative = TestData.Location(l => l.Name = "Hessdorf");
        _db.Locations.Add(alternative);
        _db.ParkingSlots.Add(TestData.Slot(alternative, "H-1"));
        _db.BlockedDays.Add(TestData.BlockedDay(preferred, Today.AddDays(1), user));
        _db.SaveChanges();

        var (booking, fallback) = await NewService().CreateBookingAsync(user.Id, null, Today.AddDays(1), TimeSlot.Morning);

        booking.LocationId.Should().Be(alternative.Id);
        fallback.Should().Contain("Erfurt").And.Contain("Hessdorf").And.Contain("location blocked on this date");
    }

    [Fact]
    public async Task CreateBookingAsync_PreferredUnavailableAndNoAlternative_ThrowsPreferredUnavailable()
    {
        var (user, preferred, _) = TestData.SeedSite(_db);
        user.PreferredLocationId = preferred.Id;
        var inactive = TestData.Location(l => l.IsActive = false);
        _db.Locations.Add(inactive);
        _db.ParkingSlots.Add(TestData.Slot(inactive, "X-1"));
        _db.BlockedDays.Add(TestData.BlockedDay(preferred, Today.AddDays(1), user));
        _db.SaveChanges();

        await ExpectValidation(() => NewService().CreateBookingAsync(user.Id, null, Today.AddDays(1), TimeSlot.Morning),
            "preferred_location_unavailable");
    }

    [Fact]
    public async Task CreateBookingAsync_ExplicitLocation_WinsOverPreferred()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var preferred = TestData.Location();
        _db.Locations.Add(preferred);
        _db.ParkingSlots.Add(TestData.Slot(preferred, "P-1"));
        user.PreferredLocationId = preferred.Id;
        _db.SaveChanges();

        var (booking, fallback) = await NewService().CreateBookingAsync(user.Id, location.Id, Today.AddDays(1), TimeSlot.Morning);

        booking.LocationId.Should().Be(location.Id);
        fallback.Should().BeNull();
    }

    // ---- booking window ----------------------------------------------------------

    [Fact]
    public void GetBookingWindow_MorningRunning_ReportsTodayAndConfiguredHorizon()
    {
        var window = NewService(maxDaysAhead: 31).GetBookingWindow();

        window.Today.Should().Be(Today);
        window.MinDate.Should().Be(Today);
        window.MaxDate.Should().Be(Today.AddDays(31));
        window.MaxDaysAhead.Should().Be(31);
        window.MorningDeadline.Should().Be("07:00");
        window.AfternoonDeadline.Should().Be("13:00");
    }

    [Fact]
    public void GetBookingWindow_AfterAfternoonEnd_MinDateIsTomorrow()
    {
        _time.Set(new DateTimeOffset(2026, 3, 2, 18, 0, 0, TimeSpan.Zero)); // 19:00 Berlin

        var window = NewService().GetBookingWindow();

        window.MinDate.Should().Be(Today.AddDays(1));
    }

    // ---- cancel ------------------------------------------------------------------

    [Fact]
    public async Task CancelBookingAsync_UnknownBooking_ThrowsNotFound()
    {
        await NewService().Invoking(s => s.CancelBookingAsync(Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CancelBookingAsync_OtherUsersBooking_ThrowsForbiddenAndKeepsBooking()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var booking = TestData.Booking(user, location, Today.AddDays(1));
        _db.Bookings.Add(booking);
        _db.SaveChanges();

        await NewService().Invoking(s => s.CancelBookingAsync(booking.Id, Guid.NewGuid()))
            .Should().ThrowAsync<ForbiddenException>();
        booking.Status.Should().Be(BookingStatus.Pending);
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.Lost)]
    public async Task CancelBookingAsync_TerminalStatus_ThrowsNotCancellable(BookingStatus status)
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var booking = TestData.Booking(user, location, Today.AddDays(1), status: status);
        _db.Bookings.Add(booking);
        _db.SaveChanges();

        await ExpectValidation(() => NewService().CancelBookingAsync(booking.Id, user.Id), "booking_not_cancellable");
        booking.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(BookingStatus.Won)]
    [InlineData(BookingStatus.Confirmed)]
    public async Task CancelBookingAsync_SlotHolder_FreesSlotAndPromotesWaitlist(BookingStatus status)
    {
        var (user, location, slots) = TestData.SeedSite(_db);
        var booking = TestData.Booking(user, location, Today.AddDays(1), TimeSlot.Afternoon, status, slots[1]);
        _db.Bookings.Add(booking);
        _db.SaveChanges();
        _time.Advance(TimeSpan.FromMinutes(5));

        var cancelled = await NewService().CancelBookingAsync(booking.Id, user.Id);

        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.ParkingSlotId.Should().BeNull();
        cancelled.CancelledAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        _waitlist.Promotions.Should().ContainSingle()
            .Which.Should().Be((location.Id, Today.AddDays(1), TimeSlot.Afternoon, slots[1].Id));
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Waitlisted)]
    public async Task CancelBookingAsync_NoSlotHeld_CancelsWithoutPromotion(BookingStatus status)
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var booking = TestData.Booking(user, location, Today.AddDays(1), status: status);
        _db.Bookings.Add(booking);
        _db.SaveChanges();

        var cancelled = await NewService().CancelBookingAsync(booking.Id, user.Id);

        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.CancelledAt.Should().NotBeNull();
        _waitlist.Promotions.Should().BeEmpty();
    }

    // ---- confirm -----------------------------------------------------------------

    [Fact]
    public async Task ConfirmBookingAsync_WonBeforeDeadline_Confirms()
    {
        var (user, location, slots) = TestData.SeedSite(_db);
        var deadline = _time.GetUtcNow().UtcDateTime.AddMinutes(1);
        var booking = TestData.Booking(user, location, Today, status: BookingStatus.Won, slot: slots[0],
            configure: b => b.ConfirmationDeadline = deadline);
        _db.Bookings.Add(booking);
        _db.SaveChanges();

        var confirmed = await NewService().ConfirmBookingAsync(booking.Id, user.Id);

        confirmed.Status.Should().Be(BookingStatus.Confirmed);
        confirmed.ConfirmedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task ConfirmBookingAsync_AtDeadline_ThrowsDeadlinePassed()
    {
        var (user, location, slots) = TestData.SeedSite(_db);
        var deadline = _time.GetUtcNow().UtcDateTime;
        var booking = TestData.Booking(user, location, Today, status: BookingStatus.Won, slot: slots[0],
            configure: b => b.ConfirmationDeadline = deadline);
        _db.Bookings.Add(booking);
        _db.SaveChanges();

        await ExpectValidation(() => NewService().ConfirmBookingAsync(booking.Id, user.Id), "confirmation_deadline_passed");
        booking.Status.Should().Be(BookingStatus.Won);
    }

    [Fact]
    public async Task ConfirmBookingAsync_NotWon_ThrowsNotConfirmable()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var booking = TestData.Booking(user, location, Today.AddDays(1), status: BookingStatus.Pending);
        _db.Bookings.Add(booking);
        _db.SaveChanges();

        await ExpectValidation(() => NewService().ConfirmBookingAsync(booking.Id, user.Id), "booking_not_confirmable");
    }
}

/// <summary>Lifecycle stand-in: records every capacity-removal call and reports no impact.</summary>
public sealed class RecordingLifecycleService : IBookingLifecycleService
{
    public List<(Guid LocationId, Guid? SlotId, BookingReleaseReason Reason, string? AdminReason)> CapacityCalls { get; } = [];

    public Task<Core.Models.CapacityChangeResult> ReleaseUserBookingsAsync(Guid userId, BookingReleaseReason reason)
        => Task.FromResult(Core.Models.CapacityChangeResult.Empty);

    public Task<Core.Models.CapacityChangeResult> HandleCapacityRemovedAsync(Guid locationId, DateOnly from, DateOnly? to,
        Guid? parkingSlotId, BookingReleaseReason reason, Guid actedByUserId, string? adminReason)
    {
        CapacityCalls.Add((locationId, parkingSlotId, reason, adminReason));
        return Task.FromResult(Core.Models.CapacityChangeResult.Empty);
    }
}

/// <summary>
/// Security review S3: the admin update endpoints and blocked-day creation must apply the
/// same blank/length rules as the create endpoints (Location.Name ≤ 100, SlotNumber ≤ 20,
/// BlockedDay.Reason ≤ 500 per the EF configuration) and answer 400 (ValidationException):
/// never store a blank name, never let an oversized value reach the column limit (500).
/// </summary>
public class AdminControllerValidationTests
{
    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly RecordingLifecycleService _lifecycle = new();
    private readonly FixedTimeProvider _time = new();

    private Api.Controllers.AdminController NewController()
    {
        var admin = TestData.Admin();
        _db.Users.Add(admin);
        _db.SaveChanges();

        return new Api.Controllers.AdminController(_db, _lifecycle, _time)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, admin.Id.ToString())], "Test"))
                }
            }
        };
    }

    [Fact]
    public async Task UpdateLocation_RejectsBlankName()
    {
        var (_, location, _) = TestData.SeedSite(_db);
        var original = location.Name;
        var controller = NewController();

        var act = () => controller.UpdateLocation(location.Id, new Api.DTOs.Admin.UpdateLocationRequest("   ", true));

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.Locations.FindAsync(location.Id))!.Name.Should().Be(original);
    }

    [Fact]
    public async Task UpdateLocation_RejectsNameOver100Characters()
    {
        var (_, location, _) = TestData.SeedSite(_db);
        var original = location.Name;
        var controller = NewController();

        var act = () => controller.UpdateLocation(location.Id, new Api.DTOs.Admin.UpdateLocationRequest(new string('x', 101), true));

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.Locations.FindAsync(location.Id))!.Name.Should().Be(original);
    }

    [Fact]
    public async Task UpdateSlot_RejectsBlankSlotNumber()
    {
        var (_, _, slots) = TestData.SeedSite(_db);
        var slot = slots[0];
        var original = slot.SlotNumber;
        var controller = NewController();

        var act = () => controller.UpdateSlot(slot.Id, new Api.DTOs.Admin.UpdateSlotRequest("   ", null, true));

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.ParkingSlots.FindAsync(slot.Id))!.SlotNumber.Should().Be(original);
    }

    [Fact]
    public async Task UpdateSlot_RejectsSlotNumberOver20Characters()
    {
        var (_, _, slots) = TestData.SeedSite(_db);
        var slot = slots[0];
        var original = slot.SlotNumber;
        var controller = NewController();

        var act = () => controller.UpdateSlot(slot.Id, new Api.DTOs.Admin.UpdateSlotRequest(new string('9', 21), null, true));

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.ParkingSlots.FindAsync(slot.Id))!.SlotNumber.Should().Be(original);
    }

    [Fact]
    public async Task CreateBlockedDay_RejectsReasonOver500Characters()
    {
        var (_, location, _) = TestData.SeedSite(_db);
        var controller = NewController();
        var request = new Api.DTOs.Admin.CreateBlockedDayRequest(location.Id, null, _time.Today.AddDays(1), new string('r', 501));

        var act = () => controller.CreateBlockedDay(request);

        await act.Should().ThrowAsync<ValidationException>();
        _db.BlockedDays.Should().BeEmpty();
        _lifecycle.CapacityCalls.Should().BeEmpty();
    }
}
