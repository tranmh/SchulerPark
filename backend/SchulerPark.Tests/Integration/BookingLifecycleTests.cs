using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Enums;
using static SchulerPark.Tests.Integration.AdminTestHelper;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Phase 20 WP1 (3.3 / 3.4): bookings no longer outlive the user, slot or location they
/// depend on. Exercised through the real HTTP endpoints against the InMemory factory.
/// </summary>
[Collection("Integration")]
public class BookingLifecycleTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BookingLifecycleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record ImpactDto(int Affected, int Reassigned, int Waitlisted, int Cancelled);
    private record BlockedDayCreated(BlockedDayDto BlockedDay, ImpactDto Impact);
    private record BlockedDayDto(Guid Id, Guid LocationId, string LocationName, Guid? ParkingSlotId, string? SlotNumber, DateOnly Date, string? Reason);

    // ── 3.3 users ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisableUser_WithWonBooking_CancelsIt_AndPromotesWaitlister()
    {
        var superAdmin = await CreateUserWithRoleAsync(_factory, _client, UserRole.SuperAdmin, "su");
        var winner = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "winner");
        var waiter = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "waiter");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = FutureDate();

        var wonId = await SeedBookingAsync(_factory, winner.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Won);
        var lostId = await SeedBookingAsync(_factory, waiter.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Lost);

        var response = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/admin/users/{winner.User.Id}/disable", superAdmin.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var won = await GetBookingAsync(_factory, wonId);
        won.Status.Should().Be(BookingStatus.Cancelled);
        won.ParkingSlotId.Should().BeNull();
        won.CancelReason.Should().Be("user_disabled");
        won.CancelledAt.Should().NotBeNull();

        var promoted = await GetBookingAsync(_factory, lostId);
        promoted.Status.Should().Be(BookingStatus.Won);
        promoted.ParkingSlotId.Should().Be(slotIds[0]);
        _factory.Emails.Sent.Should().Contain(("WaitlistWon", lostId));
    }

    [Fact]
    public async Task SelfDelete_WithPendingBooking_CancelsIt()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "selfdel");
        var (locationId, _) = await SeedLocationAsync(_factory, 2);
        var date = FutureDate();
        var pendingId = await SeedBookingAsync(_factory, user.User.Id, locationId, null, date, TimeSlot.Afternoon, BookingStatus.Pending);

        var response = await _client.SendAsync(Authed(HttpMethod.Delete, "/api/profile/data", user.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var booking = await GetBookingAsync(_factory, pendingId);
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancelReason.Should().Be("user_deleted");
    }

    [Fact]
    public async Task HardDeleteUser_PromotesWaitlister_BeforeCascade()
    {
        var superAdmin = await CreateUserWithRoleAsync(_factory, _client, UserRole.SuperAdmin, "su");
        var victim = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "victim");
        var waiter = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "waiter");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = FutureDate();

        await SeedBookingAsync(_factory, victim.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Confirmed);
        var lostId = await SeedBookingAsync(_factory, waiter.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Lost);

        var response = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/users/{victim.User.Id}", superAdmin.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var promoted = await GetBookingAsync(_factory, lostId);
        promoted.Status.Should().Be(BookingStatus.Won);
        promoted.ParkingSlotId.Should().Be(slotIds[0]);
    }

    // ── 3.4 capacity ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BlockSlot_AfterLottery_WithFreeSlot_MovesBooking()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "mover");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 2);
        var date = FutureDate();
        await SeedLotteryRunAsync(_factory, locationId, date, TimeSlot.Morning);
        var bookingId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Confirmed);

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken, new
        {
            locationId, parkingSlotId = slotIds[0], date = date.ToString("yyyy-MM-dd"), reason = "Repairs"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<BlockedDayCreated>())!;
        created.Impact.Should().Be(new ImpactDto(1, 1, 0, 0));
        created.BlockedDay.SlotNumber.Should().Be("P001");

        var booking = await GetBookingAsync(_factory, bookingId);
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ParkingSlotId.Should().Be(slotIds[1]);
        _factory.Emails.Sent.Should().Contain(("SlotReassigned", bookingId));
    }

    [Fact]
    public async Task BlockSlot_AfterLottery_NoFreeSlot_WaitlistsBooking()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "loser");
        var other = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "other");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 2);
        var date = FutureDate();
        await SeedLotteryRunAsync(_factory, locationId, date, TimeSlot.Morning);
        var bookingId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Won);
        await SeedBookingAsync(_factory, other.User.Id, locationId, slotIds[1], date, TimeSlot.Morning, BookingStatus.Confirmed);

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken, new
        {
            locationId, parkingSlotId = slotIds[0], date = date.ToString("yyyy-MM-dd")
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<BlockedDayCreated>())!;
        created.Impact.Should().Be(new ImpactDto(1, 0, 1, 0));

        var booking = await GetBookingAsync(_factory, bookingId);
        booking.Status.Should().Be(BookingStatus.Lost);
        booking.ParkingSlotId.Should().BeNull();
        _factory.Emails.Sent.Should().Contain(("SlotWithdrawn", bookingId));
    }

    [Fact]
    public async Task BlockWholeLocation_CancelsLiveBookings_WithReason()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "blocked");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 2);
        var date = FutureDate();
        var pendingId = await SeedBookingAsync(_factory, user.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Pending);
        var confirmedId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], date, TimeSlot.Afternoon, BookingStatus.Confirmed);
        var otherDayId = await SeedBookingAsync(_factory, user.User.Id, locationId, null, date.AddDays(1), TimeSlot.Morning, BookingStatus.Pending);

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken, new
        {
            locationId, date = date.ToString("yyyy-MM-dd"), reason = "Company event"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<BlockedDayCreated>())!;
        created.Impact.Should().Be(new ImpactDto(2, 0, 0, 2));

        var pending = await GetBookingAsync(_factory, pendingId);
        pending.Status.Should().Be(BookingStatus.Cancelled);
        pending.CancelReason.Should().Be("Company event");
        pending.CancelledByUserId.Should().Be(admin.User.Id);

        var confirmed = await GetBookingAsync(_factory, confirmedId);
        confirmed.Status.Should().Be(BookingStatus.Cancelled);
        confirmed.ParkingSlotId.Should().BeNull();

        (await GetBookingAsync(_factory, otherDayId)).Status.Should().Be(BookingStatus.Pending);
        _factory.Emails.Sent.Should().Contain(("CancelledByAdmin", pendingId));
        _factory.Emails.Sent.Should().Contain(("CancelledByAdmin", confirmedId));
    }

    [Fact]
    public async Task DeactivateLocation_CancelsFutureConfirmedBooking_KeepsPastOnes()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "deact");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var futureId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], FutureDate(3), TimeSlot.Morning, BookingStatus.Confirmed);
        var pastId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0],
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), TimeSlot.Morning, BookingStatus.Confirmed);

        var response = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/locations/{locationId}", admin.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var impact = (await response.Content.ReadFromJsonAsync<ImpactDto>())!;
        impact.Cancelled.Should().Be(1);

        (await GetBookingAsync(_factory, futureId)).Status.Should().Be(BookingStatus.Cancelled);
        (await GetBookingAsync(_factory, pastId)).Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task DeactivateSlot_MovesBookingToFreeSlot()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "slotdeact");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 2);
        var bookingId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], FutureDate(4), TimeSlot.Afternoon, BookingStatus.Won);

        var response = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/slots/{slotIds[0]}", admin.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var impact = (await response.Content.ReadFromJsonAsync<ImpactDto>())!;
        impact.Should().Be(new ImpactDto(1, 1, 0, 0));

        var booking = await GetBookingAsync(_factory, bookingId);
        booking.Status.Should().Be(BookingStatus.Won);
        booking.ParkingSlotId.Should().Be(slotIds[1]);
    }

    [Fact]
    public async Task BlockedDay_WithSlotFromAnotherLocation_Returns400()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var (locationA, _) = await SeedLocationAsync(_factory, 1);
        var (_, slotsB) = await SeedLocationAsync(_factory, 1);

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken, new
        {
            locationId = locationA, parkingSlotId = slotsB[0], date = FutureDate().ToString("yyyy-MM-dd")
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminBookings_AcceptsCommaSeparatedStatusFilter()
    {
        var admin = await CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "admin");
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "filter");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 3);
        var date = FutureDate(6);
        await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Won);
        await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[1], date, TimeSlot.Afternoon, BookingStatus.Confirmed);
        await SeedBookingAsync(_factory, user.User.Id, locationId, null, date.AddDays(1), TimeSlot.Morning, BookingStatus.Pending);

        var response = await _client.SendAsync(Authed(HttpMethod.Get,
            $"/api/admin/bookings?locationId={locationId}&status=Won,Confirmed", admin.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().Be(2);
    }
}
