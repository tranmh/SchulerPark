using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Infrastructure.Services.Placement;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Admin HTTP surface (AdminController, UserApprovalController, UsersAdminController) on the
/// real PostgreSQL schema: role gates, validation → ProblemDetails, unique indexes, and the
/// booking impact of slot / location / blocked-day changes.
/// </summary>
[Collection("PgApi")]
[Trait("Category", "Integration")]
public class PgAdminControllerTests
{
    private readonly PgApiFixture _fx;

    public PgAdminControllerTests(PgApiFixture fx) => _fx = fx;

    private PgWebApplicationFactory Factory => _fx.Factory;

    private static string UniqueName(string label) => $"{label}-{Guid.NewGuid():N}"[..30];

    private async Task<(HttpClient Client, AuthTestHelper.AuthResult Auth)> AsAsync(UserRole role)
    {
        var client = Factory.CreateClient();
        var auth = await AdminTestHelper.CreateUserWithRoleAsync(Factory, client, role, role.ToString().ToLowerInvariant());
        return (client, auth);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, string detailFragment)
    {
        // B41: ValidationException → RFC 9457 ProblemDetails with a traceId.
        // (The media type is asserted separately: ValidationError_ResponseMediaType_IsProblemJson.)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await JsonAsync(response);
        json.GetProperty("title").GetString().Should().Be("Bad Request");
        json.GetProperty("type").GetString().Should().StartWith("https://tools.ietf.org/html/rfc9110");
        json.GetProperty("status").GetInt32().Should().Be(400);
        json.GetProperty("detail").GetString().Should().Contain(detailFragment);
        json.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    private async Task<Guid> RegisterPendingAsync(HttpClient client, string? company = "Partner GmbH")
    {
        var email = $"ext-{Guid.NewGuid():N}@extern-partner.example";
        (await client.PostAsJsonAsync("/api/auth/register", new
        {
            email, firstName = "Paula", lastName = "Partner", company, password = AuthTestHelper.DefaultPassword
        })).EnsureSuccessStatusCode();
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        Guid id = default;
        await AdminTestHelper.WithDbAsync(Factory, async db => id = (await db.Users.SingleAsync(u => u.Email == email)).Id);
        return id;
    }

    private async Task<User> UserByIdAsync(Guid id)
    {
        User? user = null;
        await AdminTestHelper.WithDbAsync(Factory, async db => user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id));
        return user!;
    }

    // ── authorization ───────────────────────────────────────────

    [SkippableTheory]
    [InlineData("/api/admin/locations")]
    [InlineData("/api/admin/bookings")]
    [InlineData("/api/admin/users/pending")]
    [InlineData("/api/admin/users")]
    public async Task AdminEndpoints_Anonymous_Return401(string url)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = Factory.CreateClient();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task AdminEndpoints_RegularUser_Return403()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, user) = await AsAsync(UserRole.User);

        foreach (var url in new[] { "/api/admin/locations", "/api/admin/lottery-runs", "/api/admin/users/pending", "/api/admin/users" })
        {
            var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, url, user.AccessToken));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }

        var create = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            user.AccessToken, new { name = UniqueName("forbidden") }));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task AdminEndpoints_AdminAndSuperAdmin_Allowed()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (adminClient, admin) = await AsAsync(UserRole.Admin);
        var (superClient, super) = await AsAsync(UserRole.SuperAdmin);

        (await adminClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/locations", admin.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await superClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/locations", super.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // User administration is SuperAdmin only; approvals are routine admin work.
        (await adminClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/users", admin.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await adminClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/users/pending", admin.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await superClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/users", super.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── locations ───────────────────────────────────────────────

    [SkippableFact]
    public async Task CreateLocation_Valid_Returns201AndIsListedAndPersisted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var name = UniqueName("loc");

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            admin.AccessToken, new { name }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var created = await JsonAsync(response);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();
        created.GetProperty("defaultAlgorithm").GetString().Should().Be("WeightedHistory");

        var list = await JsonAsync(await client.SendAsync(
            AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/locations", admin.AccessToken)));
        list.EnumerateArray().Should().Contain(l => l.GetProperty("id").GetGuid() == id
            && l.GetProperty("name").GetString() == name
            && l.GetProperty("totalSlots").GetInt32() == 0);
    }

    [SkippableTheory]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    public async Task CreateLocation_BlankName_Returns400ProblemDetails(string name, string detail)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            admin.AccessToken, new { name }));

        await AssertProblemDetailsAsync(response, detail);
    }

    [SkippableFact]
    public async Task CreateLocation_NameOver100Chars_Returns400ProblemDetails()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            admin.AccessToken, new { name = new string('x', 101) }));

        await AssertProblemDetailsAsync(response, "100");
    }

    [SkippableFact]
    public async Task ValidationError_ResponseMediaType_IsProblemJson()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            admin.AccessToken, new { name = "" }));

        // B41: RFC 9457 ProblemDetails use the application/problem+json media type.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [SkippableFact]
    public async Task CreateLocation_DuplicateName_ReturnsClientError()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var name = UniqueName("dup");
        (await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            admin.AccessToken, new { name }))).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations",
            admin.AccessToken, new { name }));

        // B41: a user-triggerable error is a 4xx ProblemDetails, not an unhandled 500.
        ((int)second.StatusCode).Should().BeInRange(400, 499);
    }

    [SkippableFact]
    public async Task UpdateLocation_DuplicateName_Returns400WithCode()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var takenName = UniqueName("taken");
        await AdminTestHelper.SeedLocationAsync(Factory, 0, takenName);
        var (otherId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 0, UniqueName("other"));

        // B5: renaming onto another location's name (even with padding) is a 400, not a 500.
        var duplicate = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/locations/{otherId}", admin.AccessToken, new { name = $"  {takenName} ", isActive = true }));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await JsonAsync(duplicate);
        problem.GetProperty("detail").GetString().Should().Contain("already exists");
        problem.GetProperty("code").GetString().Should().Be("location_name_taken");

        // A location may keep (or re-submit) its own name.
        var selfName = UniqueName("self");
        var (selfId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 0, selfName);
        var self = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/locations/{selfId}", admin.AccessToken, new { name = selfName, isActive = true }));
        self.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task UpdateSlot_DuplicateNumberInLocation_Returns400WithCode()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (_, slotIds) = await AdminTestHelper.SeedLocationAsync(Factory, 2); // P001, P002

        // B4: renumbering onto a sibling's number (even with padding) is a 400, not a 500.
        var duplicate = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/slots/{slotIds[1]}", admin.AccessToken, new { slotNumber = " P001 ", isActive = true }));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await JsonAsync(duplicate);
        problem.GetProperty("detail").GetString().Should().Contain("already exists");
        problem.GetProperty("code").GetString().Should().Be("slot_number_taken");

        // A slot may keep its own number; the same number in another location is fine.
        var self = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/slots/{slotIds[1]}", admin.AccessToken, new { slotNumber = "P002", label = "renamed", isActive = true }));
        self.StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, otherSlotIds) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var elsewhere = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/slots/{otherSlotIds[0]}", admin.AccessToken, new { slotNumber = "P002", isActive = true }));
        elsewhere.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task UpdateLocation_UnknownId_Returns404()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/locations/{Guid.NewGuid()}", admin.AccessToken, new { name = "x", isActive = true }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task SetAlgorithm_ValidAndInvalid_PersistsOrRejects()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1);

        var ok = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/locations/{locationId}/algorithm", admin.AccessToken, new { algorithm = "roundrobin" }));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(ok)).GetProperty("algorithm").GetString().Should().Be("RoundRobin");

        var bad = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/locations/{locationId}/algorithm", admin.AccessToken, new { algorithm = "42" }));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missing = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/locations/{Guid.NewGuid()}/algorithm", admin.AccessToken, new { algorithm = "PureRandom" }));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.Locations.SingleAsync(l => l.Id == locationId)).DefaultAlgorithm
                .Should().Be(LotteryAlgorithm.RoundRobin));
    }

    [SkippableFact]
    public async Task DeactivateLocation_WithFutureBooking_CancelsItWithReason()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (_, user) = await AsAsync(UserRole.User);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var bookingId = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[0],
            AdminTestHelper.FutureDate(6), TimeSlot.Morning, BookingStatus.Won);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete,
            $"/api/admin/locations/{locationId}", admin.AccessToken));

        // B38
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var impact = await JsonAsync(response);
        impact.GetProperty("affected").GetInt32().Should().Be(1);
        impact.GetProperty("cancelled").GetInt32().Should().Be(1);

        var booking = await AdminTestHelper.GetBookingAsync(Factory, bookingId);
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.ParkingSlotId.Should().BeNull();
        booking.CancelReason.Should().Be("location_deactivated");
        booking.CancelledByUserId.Should().Be(admin.User.Id);
        booking.CancelledAt.Should().NotBeNull();
        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.Locations.SingleAsync(l => l.Id == locationId)).IsActive.Should().BeFalse());

        // Idempotent: a second deactivation changes nothing.
        var again = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete,
            $"/api/admin/locations/{locationId}", admin.AccessToken));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(again)).GetProperty("affected").GetInt32().Should().Be(0);
    }

    // ── slots ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task CreateSlot_ValidAndInvalid_CreatesOrRejects()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 0);

        var created = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots",
            admin.AccessToken, new { locationId, slotNumber = "B7", label = "near door" }));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var slot = await JsonAsync(created);
        slot.GetProperty("slotNumber").GetString().Should().Be("B7");
        slot.GetProperty("isActive").GetBoolean().Should().BeTrue();

        var list = await JsonAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get,
            $"/api/admin/slots?locationId={locationId}", admin.AccessToken)));
        list.EnumerateArray().Select(s => s.GetProperty("slotNumber").GetString()).Should().Equal("B7");

        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots",
            admin.AccessToken, new { locationId, slotNumber = " " })), "required");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots",
            admin.AccessToken, new { locationId, slotNumber = new string('9', 21) })), "20");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots",
            admin.AccessToken, new { locationId = Guid.NewGuid(), slotNumber = "X1" })), "Location not found");
    }

    [SkippableFact]
    public async Task CreateSlot_DuplicateNumberInLocation_ReturnsClientError()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1); // seeds P001

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots",
            admin.AccessToken, new { locationId, slotNumber = "P001" }));

        // B41: a user-triggerable error is a 4xx ProblemDetails, not an unhandled 500.
        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }

    [SkippableFact]
    public async Task DeactivateSlot_HeldByWonBooking_ReassignsToFreeSlot()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (_, user) = await AsAsync(UserRole.User);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var bookingId = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[0],
            AdminTestHelper.FutureDate(6), TimeSlot.Afternoon, BookingStatus.Won);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete,
            $"/api/admin/slots/{slots[0]}", admin.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var impact = await JsonAsync(response);
        impact.GetProperty("affected").GetInt32().Should().Be(1);
        impact.GetProperty("reassigned").GetInt32().Should().Be(1);
        impact.GetProperty("waitlisted").GetInt32().Should().Be(0);

        var booking = await AdminTestHelper.GetBookingAsync(Factory, bookingId);
        booking.Status.Should().Be(BookingStatus.Won);
        booking.ParkingSlotId.Should().Be(slots[1]);
        Factory.Emails.Sent.Should().Contain(("SlotReassigned", bookingId));
    }

    [SkippableFact]
    public async Task UpdateSlot_DeactivateWithNoFreeSlot_WaitlistsBooking()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (_, user) = await AsAsync(UserRole.User);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var bookingId = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[0],
            AdminTestHelper.FutureDate(7), TimeSlot.Morning, BookingStatus.Confirmed);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put,
            $"/api/admin/slots/{slots[0]}", admin.AccessToken, new { slotNumber = "P001", label = "closed", isActive = false }));

        // B32/B38: a withdrawn slot sends the booking to the waitlist.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await JsonAsync(response);
        dto.GetProperty("isActive").GetBoolean().Should().BeFalse();
        dto.GetProperty("label").GetString().Should().Be("closed");

        var booking = await AdminTestHelper.GetBookingAsync(Factory, bookingId);
        booking.Status.Should().Be(BookingStatus.Waitlisted);
        booking.ParkingSlotId.Should().BeNull();
        booking.ConfirmationDeadline.Should().BeNull();
    }

    // ── blocked days ────────────────────────────────────────────

    [SkippableFact]
    public async Task CreateBlockedDay_WholeLocation_CancelsBookingsAndCanBeRemoved()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (_, user) = await AsAsync(UserRole.User);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var date = AdminTestHelper.FutureDate(8);
        var won = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[0],
            date, TimeSlot.Morning, BookingStatus.Won);
        var otherDay = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[0],
            date.AddDays(1), TimeSlot.Morning, BookingStatus.Won);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId, date = date.ToString("yyyy-MM-dd"), reason = "Betriebsfeier" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await JsonAsync(response);
        var blockedId = body.GetProperty("blockedDay").GetProperty("id").GetGuid();
        body.GetProperty("blockedDay").GetProperty("parkingSlotId").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("impact").GetProperty("cancelled").GetInt32().Should().Be(1);

        var cancelled = await AdminTestHelper.GetBookingAsync(Factory, won);
        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.CancelReason.Should().Be("Betriebsfeier");
        cancelled.CancelledByUserId.Should().Be(admin.User.Id);
        (await AdminTestHelper.GetBookingAsync(Factory, otherDay)).Status.Should().Be(BookingStatus.Won);

        var list = await JsonAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get,
            $"/api/admin/blocked-days?locationId={locationId}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}", admin.AccessToken)));
        list.EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).Should().Equal(blockedId);

        (await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/admin/blocked-days/{blockedId}", admin.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/admin/blocked-days/{blockedId}", admin.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task CreateBlockedDay_InvalidInput_Returns400ProblemDetails()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var (_, otherSlots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var date = AdminTestHelper.FutureDate(9).ToString("yyyy-MM-dd");

        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId, date = "2020-01-01" })), "past");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId = Guid.NewGuid(), date })), "Location not found");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId, parkingSlotId = Guid.NewGuid(), date })), "Parking slot not found");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId, parkingSlotId = otherSlots[0], date })), "does not belong");

        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.BlockedDays.AnyAsync(b => b.LocationId == locationId)).Should().BeFalse());
    }

    [SkippableFact]
    public async Task CreateBlockedDay_SameLocationDateTwice_ReturnsClientError()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var date = AdminTestHelper.FutureDate(10).ToString("yyyy-MM-dd");
        (await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId, date }))).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days",
            admin.AccessToken, new { locationId, date }));

        ((int)second.StatusCode).Should().BeInRange(400, 499);
    }

    // ── grid ────────────────────────────────────────────────────

    [SkippableFact]
    public async Task SaveGrid_ValidLayoutPersists_InvalidLayoutRejected()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var url = $"/api/admin/locations/{locationId}/grid";

        var saved = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, admin.AccessToken, new
        {
            gridRows = 2,
            gridColumns = 3,
            slotPositions = new[] { new { slotId = slots[0], row = 0, column = 0 }, new { slotId = slots[1], row = 0, column = 1 } },
            cells = new[] { new { row = 1, column = 0, cellType = "road", label = (string?)null } }
        }));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var grid = await JsonAsync(saved);
        grid.GetProperty("gridRows").GetInt32().Should().Be(2);
        grid.GetProperty("gridColumns").GetInt32().Should().Be(3);
        grid.GetProperty("cells").EnumerateArray().Single().GetProperty("cellType").GetString().Should().Be("Road");
        grid.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == slots[1])
            .GetProperty("column").GetInt32().Should().Be(1);

        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, admin.AccessToken, new
        {
            gridRows = 0, gridColumns = 3, slotPositions = Array.Empty<object>(), cells = Array.Empty<object>()
        })), "rows");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, admin.AccessToken, new
        {
            gridRows = 2, gridColumns = 2,
            slotPositions = new[] { new { slotId = slots[0], row = 0, column = 0 } },
            cells = new[] { new { row = 0, column = 0, cellType = "Road", label = (string?)null } }
        })), "Duplicate position");
        await AssertProblemDetailsAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, admin.AccessToken, new
        {
            gridRows = 2, gridColumns = 2, slotPositions = Array.Empty<object>(),
            cells = new[] { new { row = 1, column = 1, cellType = "Lava", label = (string?)null } }
        })), "Invalid cell type");

        // The rejected saves left the stored layout untouched.
        var reloaded = await JsonAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, url, admin.AccessToken)));
        reloaded.GetProperty("gridColumns").GetInt32().Should().Be(3);
        reloaded.GetProperty("cells").GetArrayLength().Should().Be(1);
    }

    // ── bookings / lottery runs ─────────────────────────────────

    [SkippableFact]
    public async Task GetBookings_FilteredByLocationAndStatusList_ReturnsMatchingPage()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (_, user) = await AsAsync(UserRole.User);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var date = AdminTestHelper.FutureDate(5);
        var won = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[0], date, TimeSlot.Morning, BookingStatus.Won);
        var confirmed = await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, slots[1], date, TimeSlot.Afternoon, BookingStatus.Confirmed);
        await AdminTestHelper.SeedBookingAsync(Factory, user.User.Id, locationId, null, date.AddDays(1), TimeSlot.Morning, BookingStatus.Pending);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get,
            $"/api/admin/bookings?locationId={locationId}&status=Won,%20confirmed&pageSize=500", admin.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await JsonAsync(response);
        json.GetProperty("totalCount").GetInt32().Should().Be(2);
        json.GetProperty("pageSize").GetInt32().Should().Be(100);
        json.GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo(new[] { won, confirmed });
    }

    [SkippableFact]
    public async Task GetLotteryRuns_FilteredByLocation_ReturnsRunWithLocationName()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var name = UniqueName("runs");
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1, name);
        await AdminTestHelper.SeedLotteryRunAsync(Factory, locationId, AdminTestHelper.FutureDate(3), TimeSlot.Afternoon);

        var json = await JsonAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get,
            $"/api/admin/lottery-runs?locationId={locationId}", admin.AccessToken)));

        json.GetProperty("totalCount").GetInt32().Should().Be(1);
        var run = json.GetProperty("lotteryRuns").EnumerateArray().Single();
        run.GetProperty("locationName").GetString().Should().Be(name);
        run.GetProperty("timeSlot").GetString().Should().Be("Afternoon");
    }

    // ── user approval ───────────────────────────────────────────

    [SkippableFact]
    public async Task ListPending_Admin_IncludesNamesAndCompany()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var pendingId = await RegisterPendingAsync(client, "Zulieferer AG");

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/users/pending", admin.AccessToken));

        // B15
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await JsonAsync(response);
        var users = json.GetProperty("users").EnumerateArray().ToList();
        json.GetProperty("totalCount").GetInt32().Should().Be(users.Count);
        var entry = users.Single(u => u.GetProperty("id").GetGuid() == pendingId);
        entry.GetProperty("firstName").GetString().Should().Be("Paula");
        entry.GetProperty("lastName").GetString().Should().Be("Partner");
        entry.GetProperty("company").GetString().Should().Be("Zulieferer AG");
        entry.GetProperty("displayName").GetString().Should().Be("Paula Partner");
        entry.GetProperty("emailVerified").GetBoolean().Should().BeTrue();
        users.Should().NotContain(u => u.GetProperty("id").GetGuid() == admin.User.Id);
    }

    [SkippableFact]
    public async Task Approve_PendingUser_StampsApproverAndMailsAndSecondDecisionRejected()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var pendingId = await RegisterPendingAsync(client);
        var url = $"/api/admin/users/{pendingId}/approval";

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, url, admin.AccessToken, new { approve = true }));

        // B16
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await UserByIdAsync(pendingId);
        user.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        user.ApprovedAt.Should().NotBeNull();
        user.ApprovedByUserId.Should().Be(admin.User.Id);
        Factory.Emails.ApprovalMails.Should().Contain(("AccountApproved", user.Email));

        // The approved user can now sign in.
        (await AuthTestHelper.LoginAsync(client, user.Email)).User.Id.Should().Be(pendingId);

        // B17: only Pending users can be decided on.
        var second = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, url, admin.AccessToken, new { approve = false }));
        await AssertProblemDetailsAsync(second, "Only pending users");
        (await UserByIdAsync(pendingId)).ApprovalStatus.Should().Be(ApprovalStatus.Approved);
    }

    [SkippableFact]
    public async Task Reject_PendingUser_KeepsAccountAsRejectedAndMails()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var pendingId = await RegisterPendingAsync(client);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post,
            $"/api/admin/users/{pendingId}/approval", admin.AccessToken, new { approve = false }));

        // B16
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await UserByIdAsync(pendingId);
        user.ApprovalStatus.Should().Be(ApprovalStatus.Rejected);
        user.DeletedAt.Should().BeNull();
        user.ApprovedByUserId.Should().BeNull();
        Factory.Emails.ApprovalMails.Should().Contain(("AccountRejected", user.Email));

        var pending = await JsonAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/admin/users/pending", admin.AccessToken)));
        pending.GetProperty("users").EnumerateArray().Should().NotContain(u => u.GetProperty("id").GetGuid() == pendingId);
    }

    [SkippableFact]
    public async Task Approval_MissingDecisionOrUnknownUserOrRegularUser_Rejected()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (userClient, user) = await AsAsync(UserRole.User);
        var pendingId = await RegisterPendingAsync(client);

        (await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post,
            $"/api/admin/users/{pendingId}/approval", admin.AccessToken, new { })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post,
            $"/api/admin/users/{Guid.NewGuid()}/approval", admin.AccessToken, new { approve = true })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await userClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Post,
            $"/api/admin/users/{pendingId}/approval", user.AccessToken, new { approve = true })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await UserByIdAsync(pendingId)).ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    // ── users admin (SuperAdmin) ────────────────────────────────

    [SkippableFact]
    public async Task ListUsers_SearchMatchesCompanyAndNamesCaseInsensitively()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, super) = await AsAsync(UserRole.SuperAdmin);
        var marker = $"Acme{Guid.NewGuid():N}"[..16];
        var pendingId = await RegisterPendingAsync(client, $"{marker} Logistik");

        async Task<List<JsonElement>> SearchAsync(string term)
        {
            var json = await JsonAsync(await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get,
                $"/api/admin/users?search={Uri.EscapeDataString(term)}&pageSize=100", super.AccessToken)));
            return json.GetProperty("users").EnumerateArray().ToList();
        }

        // B19: company, case-insensitive, and the DTO exposes the Phase 21 fields.
        var byCompany = await SearchAsync(marker.ToUpperInvariant());
        var hit = byCompany.Should().ContainSingle().Subject;
        hit.GetProperty("id").GetGuid().Should().Be(pendingId);
        hit.GetProperty("company").GetString().Should().Be($"{marker} Logistik");
        hit.GetProperty("firstName").GetString().Should().Be("Paula");
        hit.GetProperty("lastName").GetString().Should().Be("Partner");
        hit.GetProperty("approvalStatus").GetString().Should().Be("Pending");

        // First / last name search reaches the same user (among others with the same names).
        (await SearchAsync("PAULA")).Should().Contain(u => u.GetProperty("id").GetGuid() == pendingId);
        (await SearchAsync("partner")).Should().Contain(u => u.GetProperty("id").GetGuid() == pendingId);
    }

    [SkippableFact]
    public async Task UpdateCompany_SuperAdminOnly_SetsTrimsAndClears()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (superClient, super) = await AsAsync(UserRole.SuperAdmin);
        var (adminClient, admin) = await AsAsync(UserRole.Admin);
        var (_, target) = await AsAsync(UserRole.User);
        var url = $"/api/admin/users/{target.User.Id}/company";

        // B20: Admin is forbidden.
        (await adminClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, admin.AccessToken, new { company = "Hijack" })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await UserByIdAsync(target.User.Id)).Company.Should().Be(AuthTestHelper.DefaultCompany);

        var set = await superClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, super.AccessToken, new { company = "  Neue Firma GmbH " }));
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await JsonAsync(set);
        dto.GetProperty("company").GetString().Should().Be("Neue Firma GmbH");
        dto.GetProperty("id").GetGuid().Should().Be(target.User.Id);
        dto.GetProperty("role").GetString().Should().Be("User");
        (await UserByIdAsync(target.User.Id)).Company.Should().Be("Neue Firma GmbH");

        (await superClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, super.AccessToken, new { company = new string('c', 201) })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UserByIdAsync(target.User.Id)).Company.Should().Be("Neue Firma GmbH");

        var cleared = await superClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, url, super.AccessToken, new { company = "   " }));
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(cleared)).GetProperty("company").ValueKind.Should().Be(JsonValueKind.Null);
        (await UserByIdAsync(target.User.Id)).Company.Should().BeNull();

        (await superClient.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/users/{Guid.NewGuid()}/company",
            super.AccessToken, new { company = "x" }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>Direct DB seeding for the hand-built service tests on the run's PostgreSQL.</summary>
public static class PgSeed
{
    public static async Task<Guid> UserAsync(PgWebApplicationFactory factory, string label = "u", DateTime? deletedAt = null)
    {
        var id = Guid.NewGuid();
        await AdminTestHelper.WithDbAsync(factory, async db =>
        {
            db.Users.Add(new User { Id = id, Email = $"{label}-{id:N}@schuler.de", DisplayName = label, EmailVerified = true,
                DeletedAt = deletedAt, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return id;
    }

    public static Task<Guid> BookingAsync(PgWebApplicationFactory f, Guid userId, Guid locationId, Guid? slotId, DateOnly date, TimeSlot slot, BookingStatus status) =>
        AdminTestHelper.SeedBookingAsync(f, userId, locationId, slotId, date, slot, status);

    public static async Task BlockAsync(PgWebApplicationFactory factory, Guid byUserId, Guid locationId, DateOnly date, Guid? slotId = null)
    {
        await AdminTestHelper.WithDbAsync(factory, async db =>
        {
            db.BlockedDays.Add(new BlockedDay { Id = Guid.NewGuid(), LocationId = locationId, ParkingSlotId = slotId, Date = date, BlockedByUserId = byUserId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
    }
}

/// <summary>LotteryService on the real PostgreSQL schema: winners, deadlines, history/run rows, idempotency, all-locations summary.</summary>
[Collection("PgApi")]
[Trait("Category", "Integration")]
public class PgSvcLotteryTests
{
    private readonly PgApiFixture _fx;
    public PgSvcLotteryTests(PgApiFixture fx) => _fx = fx;
    private PgWebApplicationFactory Factory => _fx.Factory;

    // 2031-03-10 20:00 UTC = 21:00 Berlin (CET): the nightly lottery for the 11th.
    private static readonly DateTimeOffset LotteryNow = new(2031, 3, 10, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2031, 3, 11);

    private sealed record Run(LotteryService Service, CapturingEmailService Email, RecordingPushService Push, IServiceScope Scope) : IDisposable
    {
        public void Dispose() => Scope.Dispose();
    }

    private Run NewService(DateTimeOffset? now = null)
    {
        var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = new CapturingEmailService();
        var push = new RecordingPushService();
        var clock = new MutableTimeProvider { UtcNow = now ?? LotteryNow };
        var placer = new PreferenceAwareSlotPlacer(new ManhattanDistanceMetric(), new Random(42));
        return new Run(new LotteryService(db, NullLogger<LotteryService>.Instance, email, push, placer, TestOptions.Booking, clock), email, push, scope);
    }

    private async Task<List<Booking>> BookingsAsync(Guid locationId, DateOnly date, TimeSlot slot)
    {
        List<Booking> r = [];
        await AdminTestHelper.WithDbAsync(Factory, async db => r = await db.Bookings.AsNoTracking().Where(b => b.LocationId == locationId && b.Date == date && b.TimeSlot == slot).ToListAsync());
        return r;
    }

    private async Task<List<LotteryRun>> RunsAsync(Guid locationId, DateOnly date)
    {
        List<LotteryRun> r = [];
        await AdminTestHelper.WithDbAsync(Factory, async db => r = await db.LotteryRuns.AsNoTracking().Where(x => x.LocationId == locationId && x.Date == date).ToListAsync());
        return r;
    }

    private async Task<List<LotteryHistory>> HistoryAsync(Guid locationId, DateOnly date)
    {
        List<LotteryHistory> r = [];
        await AdminTestHelper.WithDbAsync(Factory, async db => r = await db.LotteryHistories.AsNoTracking().Where(h => h.LocationId == locationId && h.Date == date).ToListAsync());
        return r;
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_Undersubscribed_AllWinWithSlotDeadlineHistoryAndRun()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 3);
        var users = new[] { await PgSeed.UserAsync(Factory), await PgSeed.UserAsync(Factory) };
        foreach (var u in users) await PgSeed.BookingAsync(Factory, u, locationId, null, Day, TimeSlot.Morning, BookingStatus.Pending);

        using var run = NewService();
        await run.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Morning);

        // B02/B03: everyone fits → Won, distinct slots, default deadline 07:00 Berlin = 06:00 UTC.
        var bookings = await BookingsAsync(locationId, Day, TimeSlot.Morning);
        bookings.Should().HaveCount(2).And.OnlyContain(b => b.Status == BookingStatus.Won);
        bookings.Select(b => b.ParkingSlotId!.Value).Should().OnlyHaveUniqueItems().And.BeSubsetOf(slots);
        var expected = DeadlineHelper.ComputeDeadline(Day, TimeSlot.Morning, LotteryNow.UtcDateTime, new BookingSettings());
        expected.Should().Be(new DateTime(2031, 3, 11, 6, 0, 0));
        bookings.Should().OnlyContain(b => b.ConfirmationDeadline == expected);
        // B04
        var history = await HistoryAsync(locationId, Day);
        history.Should().HaveCount(2).And.OnlyContain(h => h.Won && h.TimeSlot == TimeSlot.Morning);
        history.Select(h => h.UserId).Should().BeEquivalentTo(users);
        var lr = (await RunsAsync(locationId, Day)).Should().ContainSingle().Subject;
        lr.TotalBookings.Should().Be(2);
        lr.AvailableSlots.Should().Be(3);
        lr.Algorithm.Should().Be(LotteryAlgorithm.PureRandom);
        lr.RanAt.Should().Be(LotteryNow.UtcDateTime);
        run.Email.Sent.Should().BeEquivalentTo(bookings.Select(b => ("LotteryWon", b.Id)));
        run.Push.Sent.Should().BeEquivalentTo(bookings.Select(b => ("LotteryWon", b.Id)));
    }

    [SkippableTheory]
    [InlineData(LotteryAlgorithm.PureRandom)]
    [InlineData(LotteryAlgorithm.WeightedHistory)]
    [InlineData(LotteryAlgorithm.RoundRobin)]
    public async Task RunLotteryForSlot_Oversubscribed_FillsEverySlotAndWaitlistsTheRest(LotteryAlgorithm algorithm)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        await AdminTestHelper.WithDbAsync(Factory, async db => { (await db.Locations.SingleAsync(l => l.Id == locationId)).DefaultAlgorithm = algorithm; await db.SaveChangesAsync(); });
        for (var i = 0; i < 5; i++)
            await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), locationId, null, Day, TimeSlot.Afternoon, BookingStatus.Pending);

        using var run = NewService();
        await run.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Afternoon);

        // B02: as many winners as slots; the rest Waitlisted (not Lost). B03: losers no slot, no deadline.
        var bookings = await BookingsAsync(locationId, Day, TimeSlot.Afternoon);
        var winners = bookings.Where(b => b.Status == BookingStatus.Won).ToList();
        var losers = bookings.Where(b => b.Status == BookingStatus.Waitlisted).ToList();
        winners.Should().HaveCount(2);
        losers.Should().HaveCount(3);
        winners.Select(b => b.ParkingSlotId!.Value).Should().BeEquivalentTo(slots);
        winners.Should().OnlyContain(b => b.ConfirmationDeadline == new DateTime(2031, 3, 11, 11, 0, 0)); // 13:00 Berlin
        losers.Should().OnlyContain(b => b.ParkingSlotId == null && b.ConfirmationDeadline == null);
        // B04
        var history = await HistoryAsync(locationId, Day);
        history.Count(h => h.Won).Should().Be(2);
        history.Count(h => !h.Won).Should().Be(3);
        var lr = (await RunsAsync(locationId, Day)).Should().ContainSingle().Subject;
        lr.Algorithm.Should().Be(algorithm);
        lr.TotalBookings.Should().Be(5);
        lr.AvailableSlots.Should().Be(2);
        run.Email.Sent.Where(s => s.Type == "LotteryLost").Select(s => s.BookingId).Should().BeEquivalentTo(losers.Select(b => b.Id));
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_NoPendingBookings_RecordsEmptyRunWithoutHistory()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 2);

        using var run = NewService();
        await run.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Morning);

        var lr = (await RunsAsync(locationId, Day)).Should().ContainSingle().Subject; // B04
        lr.TotalBookings.Should().Be(0);
        lr.TimeSlot.Should().Be(TimeSlot.Morning);
        (await HistoryAsync(locationId, Day)).Should().BeEmpty();
        run.Email.Sent.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_AlreadyRan_SkipsWithoutTouchingBookings()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var first = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), locationId, null, Day, TimeSlot.Morning, BookingStatus.Pending);
        using (var run = NewService()) await run.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Morning);
        (await AdminTestHelper.GetBookingAsync(Factory, first)).Status.Should().Be(BookingStatus.Won);
        var late = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), locationId, null, Day, TimeSlot.Morning, BookingStatus.Pending);

        using var second = NewService(LotteryNow.AddMinutes(5));
        await second.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Morning);

        // B01
        (await AdminTestHelper.GetBookingAsync(Factory, late)).Status.Should().Be(BookingStatus.Pending);
        (await AdminTestHelper.GetBookingAsync(Factory, first)).Status.Should().Be(BookingStatus.Won);
        (await RunsAsync(locationId, Day)).Should().ContainSingle();
        (await HistoryAsync(locationId, Day)).Should().ContainSingle();
        second.Email.Sent.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_LocationBlockedThatDay_EveryoneWaitlisted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        await PgSeed.BlockAsync(Factory, await PgSeed.UserAsync(Factory, "admin"), locationId, Day);
        var id = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), locationId, null, Day, TimeSlot.Morning, BookingStatus.Pending);

        using var run = NewService();
        await run.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Morning);

        var b = await AdminTestHelper.GetBookingAsync(Factory, id);
        b.Status.Should().Be(BookingStatus.Waitlisted);
        b.ParkingSlotId.Should().BeNull();
        b.ConfirmationDeadline.Should().BeNull();
        (await RunsAsync(locationId, Day)).Single().AvailableSlots.Should().Be(0);
        (await HistoryAsync(locationId, Day)).Should().ContainSingle().Which.Won.Should().BeFalse();
        run.Email.Sent.Should().Equal(("LotteryLost", id));
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_DisabledUsersBooking_NeverWinsASlot()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var disabled = await PgSeed.UserAsync(Factory, "gone", DateTime.UtcNow);
        var ignored = await PgSeed.BookingAsync(Factory, disabled, locationId, null, Day, TimeSlot.Morning, BookingStatus.Pending);
        var active = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), locationId, null, Day, TimeSlot.Morning, BookingStatus.Pending);

        using var run = NewService();
        await run.Service.RunLotteryForSlotAsync(locationId, Day, TimeSlot.Morning);

        (await AdminTestHelper.GetBookingAsync(Factory, active)).Status.Should().Be(BookingStatus.Won);
        var b = await AdminTestHelper.GetBookingAsync(Factory, ignored);
        b.Status.Should().NotBe(BookingStatus.Won);
        b.ParkingSlotId.Should().BeNull();
        (await RunsAsync(locationId, Day)).Single().TotalBookings.Should().Be(1);
    }

    [SkippableFact]
    public async Task RunAll_ActiveLocationsBothSlots_InactiveSkipped_SummaryAndIdempotent()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var date = new DateOnly(2031, 4, 22);
        var (active, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var (inactive, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        await AdminTestHelper.WithDbAsync(Factory, async db => { (await db.Locations.SingleAsync(l => l.Id == inactive)).IsActive = false; await db.SaveChangesAsync(); });
        var morning = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), active, null, date, TimeSlot.Morning, BookingStatus.Pending);
        var afternoon = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), active, null, date, TimeSlot.Afternoon, BookingStatus.Pending);
        var inactivePending = await PgSeed.BookingAsync(Factory, await PgSeed.UserAsync(Factory), inactive, null, date, TimeSlot.Morning, BookingStatus.Pending);
        var activeCount = 0;
        await AdminTestHelper.WithDbAsync(Factory, async db => activeCount = await db.Locations.CountAsync(l => l.IsActive));

        using var run = NewService(new DateTimeOffset(2031, 4, 21, 19, 0, 0, TimeSpan.Zero));
        var summary = await run.Service.RunAllLotteriesAsync(date);

        // B09
        summary.Date.Should().Be(date);
        summary.HasFailures.Should().BeFalse();
        summary.Succeeded.Should().Be(activeCount * 2);
        (await RunsAsync(active, date)).Select(r => r.TimeSlot).Should().BeEquivalentTo(new[] { TimeSlot.Morning, TimeSlot.Afternoon });
        (await RunsAsync(inactive, date)).Should().BeEmpty();
        var won = await AdminTestHelper.GetBookingAsync(Factory, morning);
        won.Status.Should().Be(BookingStatus.Won);
        won.ParkingSlotId.Should().Be(slots[0]);
        (await AdminTestHelper.GetBookingAsync(Factory, afternoon)).Status.Should().Be(BookingStatus.Won);
        (await AdminTestHelper.GetBookingAsync(Factory, inactivePending)).Status.Should().Be(BookingStatus.Pending);

        // B01: the whole night again changes nothing.
        var again = await run.Service.RunAllLotteriesAsync(date);
        again.HasFailures.Should().BeFalse();
        again.Succeeded.Should().Be(activeCount * 2);
        (await RunsAsync(active, date)).Should().HaveCount(2);
        (await HistoryAsync(active, date)).Should().HaveCount(2);
    }
}

/// <summary>BookingLifecycleService (hand-built, real WaitlistService, pinned clock) on the run's PostgreSQL.</summary>
[Collection("PgApi")]
[Trait("Category", "Integration")]
public class PgSvcLifecycleTests
{
    private readonly PgApiFixture _fx;
    public PgSvcLifecycleTests(PgApiFixture fx) => _fx = fx;
    private PgWebApplicationFactory Factory => _fx.Factory;

    // 2031-05-12 08:00 UTC = 10:00 Berlin (CEST) → Berlin today = 2031-05-12.
    private static readonly DateTimeOffset Now = new(2031, 5, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2031, 5, 12);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    private sealed record Svc(BookingLifecycleService Service, CapturingEmailService Email, RecordingPushService Push, IServiceScope Scope) : IDisposable
    {
        public void Dispose() => Scope.Dispose();
    }

    private Svc NewService()
    {
        var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = new CapturingEmailService();
        var push = new RecordingPushService();
        var clock = new MutableTimeProvider { UtcNow = Now };
        var waitlist = new WaitlistService(db, email, push, TestOptions.Booking, clock, NullLogger<WaitlistService>.Instance);
        var placer = new PreferenceAwareSlotPlacer(new ManhattanDistanceMetric(), new Random(7));
        return new Svc(new BookingLifecycleService(db, waitlist, placer, email, push, clock, NullLogger<BookingLifecycleService>.Instance), email, push, scope);
    }

    [SkippableFact]
    public async Task ReleaseUserBookings_CancelsTodayAndLater_PromotesWaitlisters_KeepsPastAndLost()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var user = await PgSeed.UserAsync(Factory, "leaving");
        var waiter = await PgSeed.UserAsync(Factory, "waiter");
        var todayWon = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Today, TimeSlot.Afternoon, BookingStatus.Won);
        var tomorrowConfirmed = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Tomorrow, TimeSlot.Morning, BookingStatus.Confirmed);
        var pending = await PgSeed.BookingAsync(Factory, user, locationId, null, Tomorrow, TimeSlot.Afternoon, BookingStatus.Pending);
        var waitlisted = await PgSeed.BookingAsync(Factory, user, locationId, null, Tomorrow.AddDays(1), TimeSlot.Morning, BookingStatus.Waitlisted);
        var past = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Yesterday, TimeSlot.Morning, BookingStatus.Confirmed);
        var lost = await PgSeed.BookingAsync(Factory, user, locationId, null, Tomorrow, TimeSlot.Morning, BookingStatus.Lost);
        var waiterToday = await PgSeed.BookingAsync(Factory, waiter, locationId, null, Today, TimeSlot.Afternoon, BookingStatus.Waitlisted);
        var waiterTomorrow = await PgSeed.BookingAsync(Factory, waiter, locationId, null, Tomorrow, TimeSlot.Morning, BookingStatus.Waitlisted);

        using var svc = NewService();
        var result = await svc.Service.ReleaseUserBookingsAsync(user, BookingReleaseReason.UserDisabled);

        // B12
        result.Should().Be(new CapacityChangeResult(4, 0, 0, 4));
        foreach (var id in new[] { todayWon, tomorrowConfirmed, pending, waitlisted })
        {
            var b = await AdminTestHelper.GetBookingAsync(Factory, id);
            b.Status.Should().Be(BookingStatus.Cancelled, $"booking {id}");
            b.ParkingSlotId.Should().BeNull();
            b.CancelReason.Should().Be("user_disabled");
            b.CancelledByUserId.Should().BeNull();
            b.CancelledAt.Should().Be(Now.UtcDateTime);
        }
        (await AdminTestHelper.GetBookingAsync(Factory, past)).Status.Should().Be(BookingStatus.Confirmed);
        (await AdminTestHelper.GetBookingAsync(Factory, lost)).Status.Should().Be(BookingStatus.Lost);
        var promotedToday = await AdminTestHelper.GetBookingAsync(Factory, waiterToday);
        promotedToday.Status.Should().Be(BookingStatus.Won);
        promotedToday.ParkingSlotId.Should().Be(slots[0]);
        var promotedTomorrow = await AdminTestHelper.GetBookingAsync(Factory, waiterTomorrow);
        promotedTomorrow.Status.Should().Be(BookingStatus.Won);
        promotedTomorrow.ParkingSlotId.Should().Be(slots[0]);
        promotedTomorrow.ConfirmationDeadline.Should().Be(new DateTime(2031, 5, 13, 5, 0, 0)); // 07:00 Berlin CEST
        svc.Email.Sent.Should().BeEquivalentTo(new[] { ("WaitlistWon", waiterToday), ("WaitlistWon", waiterTomorrow) });
    }

    [SkippableFact]
    public async Task ReleaseUserBookings_NothingLive_ReturnsEmpty()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var user = await PgSeed.UserAsync(Factory);
        var past = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Yesterday, TimeSlot.Morning, BookingStatus.Won);

        using var svc = NewService();
        (await svc.Service.ReleaseUserBookingsAsync(user, BookingReleaseReason.UserDeleted)).Should().Be(CapacityChangeResult.Empty);
        (await AdminTestHelper.GetBookingAsync(Factory, past)).Status.Should().Be(BookingStatus.Won);
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_WholeLocationRange_CancelsWithTrimmedAdminTextAndActor()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var admin = await PgSeed.UserAsync(Factory, "admin");
        var user = await PgSeed.UserAsync(Factory);
        var pending = await PgSeed.BookingAsync(Factory, user, locationId, null, Tomorrow, TimeSlot.Morning, BookingStatus.Pending);
        var won = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Tomorrow, TimeSlot.Afternoon, BookingStatus.Won);
        var waitlisted = await PgSeed.BookingAsync(Factory, user, locationId, null, Tomorrow, TimeSlot.Afternoon, BookingStatus.Waitlisted);
        var outsideRange = await PgSeed.BookingAsync(Factory, user, locationId, slots[1], Tomorrow.AddDays(1), TimeSlot.Morning, BookingStatus.Confirmed);

        using var svc = NewService();
        var result = await svc.Service.HandleCapacityRemovedAsync(locationId, Tomorrow, Tomorrow, null, BookingReleaseReason.LocationBlocked, admin, "  Betriebsfeier  ");

        // B13
        result.Should().Be(new CapacityChangeResult(3, 0, 0, 3));
        foreach (var id in new[] { pending, won, waitlisted })
        {
            var b = await AdminTestHelper.GetBookingAsync(Factory, id);
            b.Status.Should().Be(BookingStatus.Cancelled);
            b.ParkingSlotId.Should().BeNull();
            b.CancelReason.Should().Be("Betriebsfeier");
            b.CancelledByUserId.Should().Be(admin);
            b.CancelledAt.Should().Be(Now.UtcDateTime);
            svc.Email.Sent.Should().Contain(("CancelledByAdmin", id));
            svc.Push.Sent.Should().Contain(("CancelledByAdmin", id));
        }
        (await AdminTestHelper.GetBookingAsync(Factory, outsideRange)).Status.Should().Be(BookingStatus.Confirmed);
        svc.Email.Sent.Should().NotContain(s => s.Type == "WaitlistWon");
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_WholeLocationNoAdminText_UsesReasonCode_OpenEnded()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var admin = await PgSeed.UserAsync(Factory, "admin");
        var user = await PgSeed.UserAsync(Factory);
        var today = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Today, TimeSlot.Afternoon, BookingStatus.Confirmed);
        var far = await PgSeed.BookingAsync(Factory, user, locationId, null, Today.AddDays(30), TimeSlot.Morning, BookingStatus.Pending);
        var past = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Yesterday, TimeSlot.Afternoon, BookingStatus.Confirmed);

        using var svc = NewService();
        var result = await svc.Service.HandleCapacityRemovedAsync(locationId, Today, null, null, BookingReleaseReason.LocationDeactivated, admin, "   ");

        result.Should().Be(new CapacityChangeResult(2, 0, 0, 2));
        (await AdminTestHelper.GetBookingAsync(Factory, today)).CancelReason.Should().Be("location_deactivated");
        (await AdminTestHelper.GetBookingAsync(Factory, far)).CancelReason.Should().Be("location_deactivated");
        (await AdminTestHelper.GetBookingAsync(Factory, past)).Status.Should().Be(BookingStatus.Confirmed);
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_NoLiveBookings_ReturnsEmptyAndSendsNothing()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 1);
        var admin = await PgSeed.UserAsync(Factory, "admin");
        var user = await PgSeed.UserAsync(Factory);
        await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Yesterday, TimeSlot.Morning, BookingStatus.Won);
        await PgSeed.BookingAsync(Factory, user, locationId, null, Tomorrow, TimeSlot.Morning, BookingStatus.Lost);

        using var svc = NewService();
        // B15
        (await svc.Service.HandleCapacityRemovedAsync(locationId, Today, null, null, BookingReleaseReason.LocationDeactivated, admin, null)).Should().Be(CapacityChangeResult.Empty);
        (await svc.Service.HandleCapacityRemovedAsync(locationId, Today, null, slots[0], BookingReleaseReason.SlotDeactivated, admin, null)).Should().Be(CapacityChangeResult.Empty);
        svc.Email.Sent.Should().BeEmpty();
        svc.Push.Sent.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_SingleSlot_ReassignsWhenFree_WaitlistsOtherwise_IgnoresOthers()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(Factory, 2);
        var admin = await PgSeed.UserAsync(Factory, "admin");
        var user = await PgSeed.UserAsync(Factory);
        var other = await PgSeed.UserAsync(Factory, "other");
        var movable = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Tomorrow, TimeSlot.Morning, BookingStatus.Won);
        var pendingSameDay = await PgSeed.BookingAsync(Factory, other, locationId, null, Tomorrow, TimeSlot.Morning, BookingStatus.Pending);
        var stuck = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Tomorrow, TimeSlot.Afternoon, BookingStatus.Confirmed);
        var occupant = await PgSeed.BookingAsync(Factory, other, locationId, slots[1], Tomorrow, TimeSlot.Afternoon, BookingStatus.Won);
        await AdminTestHelper.WithDbAsync(Factory, async db =>
        {
            var b = await db.Bookings.SingleAsync(x => x.Id == stuck);
            b.ReminderSentAt = Now.UtcDateTime;
            b.AutoConfirmReason = "direct_assignment";
            await db.SaveChangesAsync();
        });
        var past = await PgSeed.BookingAsync(Factory, user, locationId, slots[0], Yesterday, TimeSlot.Morning, BookingStatus.Won);

        using var svc = NewService();
        var result = await svc.Service.HandleCapacityRemovedAsync(locationId, Today, null, slots[0], BookingReleaseReason.SlotDeactivated, admin, null);

        // B14
        result.Should().Be(new CapacityChangeResult(2, 1, 1, 0));
        var moved = await AdminTestHelper.GetBookingAsync(Factory, movable);
        moved.Status.Should().Be(BookingStatus.Won);
        moved.ParkingSlotId.Should().Be(slots[1]);
        svc.Email.Sent.Should().Contain(("SlotReassigned", movable));
        svc.Push.Sent.Should().Contain(("SlotReassigned", movable));
        var w = await AdminTestHelper.GetBookingAsync(Factory, stuck);
        w.Status.Should().Be(BookingStatus.Waitlisted);
        w.ParkingSlotId.Should().BeNull();
        w.ConfirmationDeadline.Should().BeNull();
        w.ConfirmedAt.Should().BeNull();
        w.ReminderSentAt.Should().BeNull();
        w.AutoConfirmReason.Should().BeNull();
        svc.Email.Sent.Should().Contain(("SlotWithdrawn", stuck));
        (await AdminTestHelper.GetBookingAsync(Factory, pendingSameDay)).Status.Should().Be(BookingStatus.Pending);
        (await AdminTestHelper.GetBookingAsync(Factory, occupant)).ParkingSlotId.Should().Be(slots[1]);
        (await AdminTestHelper.GetBookingAsync(Factory, past)).ParkingSlotId.Should().Be(slots[0]);
        svc.Email.Sent.Should().NotContain(s => s.Type == "CancelledByAdmin");
    }

    [Theory]
    [InlineData(BookingReleaseReason.UserDisabled, "user_disabled")]
    [InlineData(BookingReleaseReason.UserDeleted, "user_deleted")]
    [InlineData(BookingReleaseReason.AdminCancelled, "admin_cancelled")]
    [InlineData(BookingReleaseReason.SlotBlocked, "slot_blocked")]
    [InlineData(BookingReleaseReason.SlotDeactivated, "slot_deactivated")]
    [InlineData(BookingReleaseReason.LocationBlocked, "location_blocked")]
    [InlineData(BookingReleaseReason.LocationDeactivated, "location_deactivated")]
    public void ReasonCode_MapsEveryReasonToSnakeCase(BookingReleaseReason reason, string expected) =>
        BookingLifecycleService.ReasonCode(reason).Should().Be(expected); // B15
}
