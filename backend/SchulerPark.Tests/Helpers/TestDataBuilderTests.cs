using SchulerPark.Core.Enums;
using SchulerPark.Tests.Integration;

namespace SchulerPark.Tests.Helpers;

public class TestDataBuilderTests
{
    [Fact]
    public void Builders_LocationWithSlotsAndBooking_ProduceLinkedGraph()
    {
        var location = TestData.LocationWithSlots(3);
        var user = TestData.User(u => u.Role = UserRole.Admin);
        var booking = TestData.Booking(user, location, new DateOnly(2026, 11, 2), TimeSlot.Afternoon);

        Assert.Equal(3, location.ParkingSlots.Count);
        Assert.All(location.ParkingSlots, s => Assert.Equal(location.Id, s.LocationId));
        Assert.Equal(new[] { "1", "2", "3" }, location.ParkingSlots.Select(s => s.SlotNumber));
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(user.Id, booking.UserId);
        Assert.Equal(location.Id, booking.LocationId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.NotEqual(user.Id, location.Id);
    }

    [Fact]
    public void FixedTimeProvider_Advance_MovesClockOnlyExplicitly()
    {
        var start = new DateTimeOffset(2026, 11, 2, 6, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(start);

        Assert.Equal(start, clock.GetUtcNow());
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(start.AddHours(1), clock.GetUtcNow());
    }
}
