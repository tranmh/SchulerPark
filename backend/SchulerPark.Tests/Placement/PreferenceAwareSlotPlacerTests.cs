namespace SchulerPark.Tests.Placement;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services.Placement;

public class PreferenceAwareSlotPlacerTests
{
    private readonly PreferenceAwareSlotPlacer _placer = new(new ManhattanDistanceMetric());

    private static Location NewLocation() => new()
    {
        Id = Guid.NewGuid(),
        GridRows = 5,
        GridColumns = 5
    };

    private static ParkingSlot Slot(Guid locationId, string number, int? row, int? col) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = locationId,
        SlotNumber = number,
        GridRow = row,
        GridColumn = col,
        IsActive = true
    };

    private static Booking Winner(Guid? preferredSlotId, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
        TimeSlot = TimeSlot.Morning,
        Status = BookingStatus.Pending,
        CreatedAt = createdAt,
        User = new User
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            DisplayName = "U",
            PreferredSlotId = preferredSlotId
        }
    };

    [Fact]
    public void SingleWinner_GetsExactPreferredSlot()
    {
        var loc = NewLocation();
        var s1 = Slot(loc.Id, "A-1", 0, 0);
        var s2 = Slot(loc.Id, "A-2", 0, 1);

        var w = Winner(s1.Id, DateTime.UtcNow);
        var result = _placer.Place([w], [s1, s2], loc, [],
            new Dictionary<Guid, ParkingSlot> { [s1.Id] = s1 });

        result[w.Id].Should().Be(s1.Id);
    }

    [Fact]
    public void TwoWinners_SamePreferred_FirstCreatedWins()
    {
        var loc = NewLocation();
        var pref = Slot(loc.Id, "A-1", 0, 0);
        var other = Slot(loc.Id, "A-2", 0, 1);

        var w1 = Winner(pref.Id, DateTime.UtcNow.AddMinutes(-10));
        var w2 = Winner(pref.Id, DateTime.UtcNow);

        var result = _placer.Place([w2, w1], [pref, other], loc, [],
            new Dictionary<Guid, ParkingSlot> { [pref.Id] = pref });

        result[w1.Id].Should().Be(pref.Id);
        result[w2.Id].Should().Be(other.Id); // nearest-available fallback
    }

    [Fact]
    public void PreferredTaken_FallsBackToNearest()
    {
        var loc = NewLocation();
        var pref = Slot(loc.Id, "A-1", 0, 0);
        var near = Slot(loc.Id, "A-2", 0, 1);
        var far = Slot(loc.Id, "A-3", 4, 4);

        var w1 = Winner(pref.Id, DateTime.UtcNow.AddMinutes(-5));
        var w2 = Winner(pref.Id, DateTime.UtcNow);

        var result = _placer.Place([w1, w2], [pref, near, far], loc, [],
            new Dictionary<Guid, ParkingSlot> { [pref.Id] = pref });

        result[w1.Id].Should().Be(pref.Id);
        result[w2.Id].Should().Be(near.Id);
    }

    [Fact]
    public void PreferredInactiveOrMissing_FallsThroughToRandom()
    {
        var loc = NewLocation();
        var s1 = Slot(loc.Id, "A-1", 0, 0);
        var s2 = Slot(loc.Id, "A-2", 1, 0);
        var missingPrefId = Guid.NewGuid();

        var w = Winner(missingPrefId, DateTime.UtcNow);
        var result = _placer.Place([w], [s1, s2], loc, [],
            new Dictionary<Guid, ParkingSlot>());

        // Assigned something (no error).
        new[] { s1.Id, s2.Id }.Should().Contain(result[w.Id]);
    }

    [Fact]
    public void PreferredAtDifferentLocation_Ignored()
    {
        var loc = NewLocation();
        var otherLocId = Guid.NewGuid();
        var prefElsewhere = Slot(otherLocId, "X-1", 0, 0);
        var s1 = Slot(loc.Id, "A-1", 0, 0);

        var w = Winner(prefElsewhere.Id, DateTime.UtcNow);
        var result = _placer.Place([w], [s1], loc, [],
            new Dictionary<Guid, ParkingSlot> { [prefElsewhere.Id] = prefElsewhere });

        result[w.Id].Should().Be(s1.Id); // falls through to random pass
    }

    [Fact]
    public void NoPreference_AllRandomPass()
    {
        var loc = NewLocation();
        var s1 = Slot(loc.Id, "A-1", 0, 0);
        var s2 = Slot(loc.Id, "A-2", 0, 1);

        var w1 = Winner(null, DateTime.UtcNow);
        var w2 = Winner(null, DateTime.UtcNow.AddMinutes(1));

        var result = _placer.Place([w1, w2], [s1, s2], loc, [],
            new Dictionary<Guid, ParkingSlot>());

        result.Should().HaveCount(2);
        result.Values.Distinct().Should().HaveCount(2);
    }
}
