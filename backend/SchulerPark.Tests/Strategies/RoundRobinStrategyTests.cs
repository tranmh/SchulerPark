namespace SchulerPark.Tests.Strategies;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services.Strategies;

public class RoundRobinStrategyTests
{
    private readonly RoundRobinStrategy _strategy = new();

    private static Booking CreateBooking(Guid userId, Guid locationId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        LocationId = locationId,
        Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
        TimeSlot = TimeSlot.Morning,
        Status = BookingStatus.Pending
    };

    private static ParkingSlot CreateSlot(Guid locationId) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = locationId,
        SlotNumber = "P001",
        IsActive = true
    };

    private static LotteryHistory CreateHistory(Guid userId, Guid locationId, DateOnly date, bool won) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        LocationId = locationId,
        Date = date,
        TimeSlot = TimeSlot.Morning,
        Won = won
    };

    [Fact]
    public void NeverWonUserGetsPriorityOverRecentWinner()
    {
        var locationId = Guid.NewGuid();
        var neverWonUser = Guid.NewGuid();
        var recentWinner = Guid.NewGuid();

        var bookings = new List<Booking>
        {
            CreateBooking(recentWinner, locationId),
            CreateBooking(neverWonUser, locationId),
        };
        var slots = new List<ParkingSlot> { CreateSlot(locationId) }; // only 1 slot

        var history = new List<LotteryHistory>
        {
            CreateHistory(recentWinner, locationId, new DateOnly(2026, 4, 7), true),
        };

        var results = _strategy.Execute(bookings, slots, history);

        results.Should().HaveCount(2);
        var neverWonResult = results.First(r => r.UserId == neverWonUser);
        var recentWinnerResult = results.First(r => r.UserId == recentWinner);

        neverWonResult.Won.Should().BeTrue();
        recentWinnerResult.Won.Should().BeFalse();
    }

    [Fact]
    public void OlderWinnerGetsPriorityOverRecentWinner()
    {
        var locationId = Guid.NewGuid();
        var oldWinner = Guid.NewGuid();
        var recentWinner = Guid.NewGuid();

        var bookings = new List<Booking>
        {
            CreateBooking(recentWinner, locationId),
            CreateBooking(oldWinner, locationId),
        };
        var slots = new List<ParkingSlot> { CreateSlot(locationId) };

        var history = new List<LotteryHistory>
        {
            CreateHistory(oldWinner, locationId, new DateOnly(2026, 3, 1), true),
            CreateHistory(recentWinner, locationId, new DateOnly(2026, 4, 7), true),
        };

        var results = _strategy.Execute(bookings, slots, history);

        results.First(r => r.UserId == oldWinner).Won.Should().BeTrue();
        results.First(r => r.UserId == recentWinner).Won.Should().BeFalse();
    }

    [Fact]
    public void AllCandidatesAppearInResults()
    {
        var locationId = Guid.NewGuid();
        var bookings = Enumerable.Range(0, 5)
            .Select(_ => CreateBooking(Guid.NewGuid(), locationId)).ToList();
        var slots = Enumerable.Range(0, 2)
            .Select(_ => CreateSlot(locationId)).ToList();

        var results = _strategy.Execute(bookings, slots, []);

        results.Should().HaveCount(5);
        results.Count(r => r.Won).Should().Be(2);
        results.Count(r => !r.Won).Should().Be(3);
    }

    [Fact]
    public void WinnersGetUniqueSlotAssignments()
    {
        var locationId = Guid.NewGuid();
        var bookings = Enumerable.Range(0, 5)
            .Select(_ => CreateBooking(Guid.NewGuid(), locationId)).ToList();
        var slots = Enumerable.Range(0, 3)
            .Select(_ => CreateSlot(locationId)).ToList();

        var results = _strategy.Execute(bookings, slots, []);

        var winnerSlots = results.Where(r => r.Won).Select(r => r.AssignedSlotId).ToList();
        winnerSlots.Should().OnlyHaveUniqueItems();
    }
}
