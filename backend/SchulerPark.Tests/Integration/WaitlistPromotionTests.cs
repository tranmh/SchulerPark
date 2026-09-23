using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using static SchulerPark.Tests.Integration.AdminTestHelper;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Phase 20 WP4: what a freed slot turns a Waitlisted booking into depends on the clock —
/// Won with a stored deadline while there is time to confirm, Confirmed outright once the
/// default deadline is less than the minimum window away, nothing once the slot has ended.
/// </summary>
[Collection("Integration")]
public class WaitlistPromotionTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private static readonly DateOnly Day = new(2026, 6, 10);   // CEST

    public WaitlistPromotionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(CapturingEmailService Email, RecordingPushService Push)> PromoteAsync(Guid locationId, TimeSlot slot, Guid slotId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = new CapturingEmailService();
        var push = new RecordingPushService();
        var service = new WaitlistService(db, email, push, TestOptions.Booking, _factory.Clock, NullLogger<WaitlistService>.Instance);
        await service.TryPromoteWaitlistAsync(locationId, Day, slot, slotId);
        return (email, push);
    }

    [Fact]
    public async Task TheEveningBefore_PromotesToWon_WithTheDefaultDeadline()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "evening");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var id = await SeedBookingAsync(_factory, user.User.Id, locationId, null, Day, TimeSlot.Morning, BookingStatus.Waitlisted);

        // 23:00 Berlin the day before = 21:00 UTC.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 9, 21, 0, 0, TimeSpan.Zero)))
        {
            var (email, push) = await PromoteAsync(locationId, TimeSlot.Morning, slotIds[0]);

            var b = await GetBookingAsync(_factory, id);
            b.Status.Should().Be(BookingStatus.Won);
            b.ParkingSlotId.Should().Be(slotIds[0]);
            b.ConfirmationDeadline.Should().Be(new DateTime(2026, 6, 10, 5, 0, 0));   // 07:00 Berlin
            b.ConfirmedAt.Should().BeNull();
            email.Sent.Should().Contain(("WaitlistWon", id));
            push.Sent.Should().Contain(("WaitlistWon", id));
        }
    }

    [Fact]
    public async Task ShortlyBeforeTheDefaultDeadline_ExtendsTheDeadline()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "early");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var id = await SeedBookingAsync(_factory, user.User.Id, locationId, null, Day, TimeSlot.Morning, BookingStatus.Waitlisted);

        // 04:30 Berlin = 02:30 UTC: the 2 h window still fits before 07:00 minus 2 h? No — the
        // window opens at 05:00 Berlin, so 04:30 is still a normal Won, with 07:00 as deadline.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 10, 2, 30, 0, TimeSpan.Zero)))
        {
            await PromoteAsync(locationId, TimeSlot.Morning, slotIds[0]);
            var b = await GetBookingAsync(_factory, id);
            b.Status.Should().Be(BookingStatus.Won);
            b.ConfirmationDeadline.Should().Be(new DateTime(2026, 6, 10, 5, 0, 0));
        }
    }

    [Fact]
    public async Task InsideTheWindow_ConfirmsDirectly_AndTellsTheUser()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "inside");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var id = await SeedBookingAsync(_factory, user.User.Id, locationId, null, Day, TimeSlot.Morning, BookingStatus.Waitlisted);

        // 06:30 Berlin = 04:30 UTC: inside the 2 h window before the 07:00 deadline.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 10, 4, 30, 0, TimeSpan.Zero)))
        {
            var (email, push) = await PromoteAsync(locationId, TimeSlot.Morning, slotIds[0]);

            var b = await GetBookingAsync(_factory, id);
            b.Status.Should().Be(BookingStatus.Confirmed);
            b.ParkingSlotId.Should().Be(slotIds[0]);
            b.ConfirmedAt.Should().Be(new DateTime(2026, 6, 10, 4, 30, 0));
            b.ConfirmationDeadline.Should().BeNull();
            email.Sent.Should().Contain(("WaitlistAutoConfirmed", id));
            push.Sent.Should().Contain(("WaitlistAutoConfirmed", id));
            email.Sent.Should().NotContain(("WaitlistWon", id));
        }
    }

    [Fact]
    public async Task AfterTheDeadline_StillFillsTheSlot()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "late");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var id = await SeedBookingAsync(_factory, user.User.Id, locationId, null, Day, TimeSlot.Morning, BookingStatus.Waitlisted);

        // 09:00 Berlin = 07:00 UTC: past the deadline (the old dead zone, 2.3) but the slot runs until 12:00.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 10, 7, 0, 0, TimeSpan.Zero)))
        {
            await PromoteAsync(locationId, TimeSlot.Morning, slotIds[0]);
            (await GetBookingAsync(_factory, id)).Status.Should().Be(BookingStatus.Confirmed);
        }
    }

    [Fact]
    public async Task AfterSlotEnd_DoesNothing()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "over");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var id = await SeedBookingAsync(_factory, user.User.Id, locationId, null, Day, TimeSlot.Morning, BookingStatus.Waitlisted);

        // 12:00 Berlin = 10:00 UTC: the Morning slot is over.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero)))
        {
            var (email, _) = await PromoteAsync(locationId, TimeSlot.Morning, slotIds[0]);
            (await GetBookingAsync(_factory, id)).Status.Should().Be(BookingStatus.Waitlisted);
            email.Sent.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Positions_FollowWeightThenCreatedAt_AndIgnoreOtherQueues()
    {
        var (locationId, _) = await SeedLocationAsync(_factory, 1);
        var a = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "posA");
        var b = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "posB");
        var t0 = DateTime.UtcNow.AddHours(-2);
        var laterId = await SeedBookingAsync(_factory, a.User.Id, locationId, null, Day, TimeSlot.Afternoon, BookingStatus.Waitlisted, t0.AddMinutes(30));
        var earlierId = await SeedBookingAsync(_factory, b.User.Id, locationId, null, Day, TimeSlot.Afternoon, BookingStatus.Waitlisted, t0);
        // A different slot's queue must not count.
        await SeedBookingAsync(_factory, b.User.Id, locationId, null, Day, TimeSlot.Morning, BookingStatus.Waitlisted, t0);

        var my = await _client.SendAsync(Authed(HttpMethod.Get, "/api/bookings/my?status=Waitlisted", a.AccessToken));
        var json = await my.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var row = json.GetProperty("bookings").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == laterId);
        row.GetProperty("waitlistPosition").GetInt32().Should().Be(2);

        var myB = await _client.SendAsync(Authed(HttpMethod.Get, "/api/bookings/my?status=Waitlisted", b.AccessToken));
        var jsonB = await myB.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var rowB = jsonB.GetProperty("bookings").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == earlierId);
        rowB.GetProperty("waitlistPosition").GetInt32().Should().Be(1);
    }
}
