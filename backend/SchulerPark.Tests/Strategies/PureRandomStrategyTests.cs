namespace SchulerPark.Tests.Strategies;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services.Strategies;

public class PureRandomStrategyTests
{
    private readonly PureRandomStrategy _strategy = new();

    private static List<Booking> CreateBookings(int count, Guid locationId) =>
        Enumerable.Range(0, count).Select(i => new Booking
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            LocationId = locationId,
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            TimeSlot = TimeSlot.Morning,
            Status = BookingStatus.Pending
        }).ToList();

    private static List<ParkingSlot> CreateSlots(int count, Guid locationId) =>
        Enumerable.Range(0, count).Select(i => new ParkingSlot
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SlotNumber = $"P{i + 1:D3}",
            IsActive = true
        }).ToList();

    [Fact]
    public void AllCandidatesAppearInResults()
    {
        var locationId = Guid.NewGuid();
        var bookings = CreateBookings(5, locationId);
        var slots = CreateSlots(3, locationId);

        var results = _strategy.Execute(bookings, slots, []);

        results.Should().HaveCount(5);
        results.Select(r => r.BookingId).Should().BeEquivalentTo(bookings.Select(b => b.Id));
    }

    [Fact]
    public void WinnerCountEqualsAvailableSlots_WhenDemandExceedsSupply()
    {
        var locationId = Guid.NewGuid();
        var bookings = CreateBookings(5, locationId);
        var slots = CreateSlots(2, locationId);

        var results = _strategy.Execute(bookings, slots, []);

        results.Count(r => r.Won).Should().Be(2);
        results.Count(r => !r.Won).Should().Be(3);
    }

    [Fact]
    public void WinnersGetUniqueSlotAssignments()
    {
        var locationId = Guid.NewGuid();
        var bookings = CreateBookings(5, locationId);
        var slots = CreateSlots(3, locationId);

        var results = _strategy.Execute(bookings, slots, []);

        var winnerSlots = results.Where(r => r.Won).Select(r => r.AssignedSlotId).ToList();
        winnerSlots.Should().OnlyHaveUniqueItems();
        winnerSlots.Should().AllSatisfy(s => s.Should().NotBeNull());
    }

    [Fact]
    public void LosersHaveNoAssignedSlot()
    {
        var locationId = Guid.NewGuid();
        var bookings = CreateBookings(5, locationId);
        var slots = CreateSlots(2, locationId);

        var results = _strategy.Execute(bookings, slots, []);

        results.Where(r => !r.Won).Should().AllSatisfy(r => r.AssignedSlotId.Should().BeNull());
    }

    [Fact]
    public void AllWin_WhenSlotsExceedCandidates()
    {
        var locationId = Guid.NewGuid();
        var bookings = CreateBookings(2, locationId);
        var slots = CreateSlots(5, locationId);

        var results = _strategy.Execute(bookings, slots, []);

        results.Should().AllSatisfy(r => r.Won.Should().BeTrue());
    }
}
