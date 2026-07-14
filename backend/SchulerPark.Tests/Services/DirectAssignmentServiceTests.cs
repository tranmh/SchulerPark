namespace SchulerPark.Tests.Services;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Infrastructure.Services.Placement;

public class DirectAssignmentServiceTests
{
    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DirectAssignmentService NewService(AppDbContext db) => new(
        db,
        new PreferenceAwareSlotPlacer(new ManhattanDistanceMetric()),
        NullLogger<DirectAssignmentService>.Instance);

    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

    private static (User User, Location Location, List<ParkingSlot> Slots) Seed(
        AppDbContext db, int slotCount, bool lotteryRan = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@schuler.de",
            DisplayName = "Unit User",
            EmailVerified = true
        };
        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = $"Loc-{Guid.NewGuid():N}",
            Address = "Somewhere",
            IsActive = true,
            DefaultAlgorithm = LotteryAlgorithm.PureRandom
        };
        var slots = Enumerable.Range(1, slotCount).Select(i => new ParkingSlot
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            SlotNumber = $"A-{i}",
            IsActive = true
        }).ToList();

        db.Users.Add(user);
        db.Locations.Add(location);
        db.ParkingSlots.AddRange(slots);
        if (lotteryRan)
        {
            db.LotteryRuns.Add(new LotteryRun
            {
                Id = Guid.NewGuid(),
                LocationId = location.Id,
                Date = Date,
                TimeSlot = TimeSlot.Morning,
                Algorithm = LotteryAlgorithm.PureRandom,
                RanAt = DateTime.UtcNow
            });
        }
        db.SaveChanges();
        return (user, location, slots);
    }

    private static Booking NewBooking(User user, Location location) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        LocationId = location.Id,
        Date = Date,
        TimeSlot = TimeSlot.Morning,
        Status = BookingStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    private static Booking OccupyingBooking(Guid locationId, Guid slotId, BookingStatus status, AppDbContext db)
    {
        var occupant = new User
        {
            Id = Guid.NewGuid(),
            Email = $"o-{Guid.NewGuid():N}@schuler.de",
            DisplayName = "Occupant",
            EmailVerified = true
        };
        db.Users.Add(occupant);
        return new Booking
        {
            Id = Guid.NewGuid(),
            UserId = occupant.Id,
            LocationId = locationId,
            ParkingSlotId = slotId,
            Date = Date,
            TimeSlot = TimeSlot.Morning,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task ApplyAsync_NoLotteryRun_ReturnsNotApplicable_AndKeepsPending()
    {
        using var db = NewDb();
        var (user, location, _) = Seed(db, 2, lotteryRan: false);
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.NotApplicable);
        booking.Status.Should().Be(BookingStatus.Pending);
        booking.ParkingSlotId.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_FreeSlot_SetsConfirmedAndConfirmedAt()
    {
        using var db = NewDb();
        var (user, location, slots) = Seed(db, 1);
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.AssignedConfirmed);
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ParkingSlotId.Should().Be(slots[0].Id);
        booking.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyAsync_SlotsHeldByWonAndConfirmed_ExcludesBoth()
    {
        using var db = NewDb();
        var (user, location, slots) = Seed(db, 3);
        db.Bookings.Add(OccupyingBooking(location.Id, slots[0].Id, BookingStatus.Won, db));
        db.Bookings.Add(OccupyingBooking(location.Id, slots[1].Id, BookingStatus.Confirmed, db));
        db.SaveChanges();
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.AssignedConfirmed);
        booking.ParkingSlotId.Should().Be(slots[2].Id);
    }

    [Fact]
    public async Task ApplyAsync_AllSlotsHeld_ReturnsWaitlistedLost()
    {
        using var db = NewDb();
        var (user, location, slots) = Seed(db, 1);
        db.Bookings.Add(OccupyingBooking(location.Id, slots[0].Id, BookingStatus.Confirmed, db));
        db.SaveChanges();
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.WaitlistedLost);
        booking.Status.Should().Be(BookingStatus.Lost);
        booking.ParkingSlotId.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_WholeLocationBlocked_ReturnsWaitlistedLost()
    {
        using var db = NewDb();
        var (user, location, _) = Seed(db, 2);
        db.BlockedDays.Add(new BlockedDay
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            ParkingSlotId = null,
            Date = Date,
            Reason = "Event",
            BlockedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.WaitlistedLost);
        booking.Status.Should().Be(BookingStatus.Lost);
    }

    [Fact]
    public async Task ApplyAsync_LostBookingsHoldNoSlots()
    {
        using var db = NewDb();
        var (user, location, slots) = Seed(db, 1);
        var lost = OccupyingBooking(location.Id, slots[0].Id, BookingStatus.Lost, db);
        lost.ParkingSlotId = null; // Lost bookings never carry a slot
        db.Bookings.Add(lost);
        db.SaveChanges();
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.AssignedConfirmed);
        booking.ParkingSlotId.Should().Be(slots[0].Id);
    }

    [Fact]
    public async Task ApplyAsync_AssignsPreferredSlotWhenFree()
    {
        using var db = NewDb();
        var (user, location, slots) = Seed(db, 3);
        user.PreferredSlotId = slots[1].Id;
        db.SaveChanges();
        var booking = NewBooking(user, location);

        var outcome = await NewService(db).ApplyAsync(booking);

        outcome.Should().Be(DirectAssignmentOutcome.AssignedConfirmed);
        booking.ParkingSlotId.Should().Be(slots[1].Id);
    }
}
