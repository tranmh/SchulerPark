namespace SchulerPark.Tests.Strategies;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services.Strategies;

public class WeightedHistoryStrategyTests
{
    private readonly WeightedHistoryStrategy _strategy = new();

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
    public void WeightCalculation_NoHistory_ReturnsBaseWeight()
    {
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var weight = WeightedHistoryStrategy.CalculateWeight(userId, locationId, []);

        weight.Should().Be(1.0);
    }

    [Fact]
    public void WeightCalculation_ThreeConsecutiveLosses_Returns2Point5()
    {
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var history = new List<LotteryHistory>
        {
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 5), false),
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 6), false),
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 7), false),
        };

        var weight = WeightedHistoryStrategy.CalculateWeight(userId, locationId, history);

        weight.Should().Be(2.5); // 1.0 + 3 * 0.5
    }

    [Fact]
    public void WeightCalculation_WinResetsConsecutiveLosses()
    {
        var userId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var history = new List<LotteryHistory>
        {
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 3), false),
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 4), false),
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 5), true),  // win resets
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 6), false),
            CreateHistory(userId, locationId, new DateOnly(2026, 4, 7), false),
        };

        var weight = WeightedHistoryStrategy.CalculateWeight(userId, locationId, history);

        weight.Should().Be(2.0); // 1.0 + 2 * 0.5 (only losses after last win)
    }

    [Fact]
    public void AllCandidatesAppearInResults()
    {
        var locationId = Guid.NewGuid();
        var bookings = Enumerable.Range(0, 5)
            .Select(_ => CreateBooking(Guid.NewGuid(), locationId)).ToList();
        var slots = new List<ParkingSlot> { CreateSlot(locationId), CreateSlot(locationId) };

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
