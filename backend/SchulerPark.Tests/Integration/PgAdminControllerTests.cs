using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

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

    [SkippableFact(Skip = "aqaa: suspected bug - a duplicate slot number in one location hits the unique (LocationId, SlotNumber) index and surfaces as 500 instead of a 4xx validation error (B41)")]
    public async Task CreateSlot_DuplicateNumberInLocation_ReturnsClientError()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var (client, admin) = await AsAsync(UserRole.Admin);
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(Factory, 1); // seeds P001

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots",
            admin.AccessToken, new { locationId, slotNumber = "P001" }));

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

    [SkippableFact(Skip = "aqaa: suspected bug - blocking the same location/date twice hits the unique (LocationId, Date) filtered index and surfaces as 500 instead of a 4xx validation error (B41)")]
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
