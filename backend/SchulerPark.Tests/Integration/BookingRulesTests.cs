using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SchulerPark.Core.Enums;
using static SchulerPark.Tests.Integration.AdminTestHelper;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Phase 20 WP3 (3.1 window, 3.2 availability semantics, 2.9 cross-location duplicates,
/// 2.1 same-day booking) plus the §5 backfill for confirm. The app clock is pinned per
/// test through <see cref="CustomWebApplicationFactory.Clock"/>.
/// </summary>
[Collection("Integration")]
public class BookingRulesTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BookingRulesTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record BookingDto(Guid Id, Guid LocationId, string Status, Guid? ParkingSlotId, DateTime? ConfirmationDeadline);
    private record WindowDto(DateOnly Today, DateOnly MinDate, DateOnly MaxDate, int MaxDaysAhead, bool MorningOpenToday, bool AfternoonOpenToday);
    private record AvailabilityDto(DateOnly Date, string TimeSlot, int AvailableSlots, int TotalSlots, int BookingCount, int PendingCount, int WaitlistCount, bool LotteryRan);

    private async Task<HttpResponseMessage> PostBookingAsync(string token, Guid locationId, DateOnly date, string timeSlot = "Morning") =>
        await _client.SendAsync(Authed(HttpMethod.Post, "/api/bookings", token, new
        {
            locationId, date = date.ToString("yyyy-MM-dd"), timeSlot
        }));

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // ── 3.1 one booking window ───────────────────────────────────────────────────

    [Fact]
    public async Task Window_UsesMaxDaysAhead_NotCalendarMonth()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "window");
        var (locationId, _) = await SeedLocationAsync(_factory, 2);

        // 2026-01-31 12:00 UTC: AddMonths(1) would have given 2026-02-28; today+31 is 2026-03-03.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero)))
        {
            var window = await _client.SendAsync(Authed(HttpMethod.Get, "/api/bookings/window", user.AccessToken));
            window.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = (await window.Content.ReadFromJsonAsync<WindowDto>())!;
            dto.Today.Should().Be(new DateOnly(2026, 1, 31));
            dto.MinDate.Should().Be(new DateOnly(2026, 1, 31));   // 13:00 Berlin — afternoon still open
            dto.MaxDate.Should().Be(new DateOnly(2026, 3, 3));
            dto.MaxDaysAhead.Should().Be(31);
            dto.MorningOpenToday.Should().BeFalse();
            dto.AfternoonOpenToday.Should().BeTrue();

            var lastDay = await PostBookingAsync(user.AccessToken, locationId, new DateOnly(2026, 3, 3));
            lastDay.StatusCode.Should().Be(HttpStatusCode.Created);

            var tooFar = await PostBookingAsync(user.AccessToken, locationId, new DateOnly(2026, 3, 4));
            tooFar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(tooFar)).Should().Be("booking_too_far_ahead");

            var past = await PostBookingAsync(user.AccessToken, locationId, new DateOnly(2026, 1, 30));
            past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(past)).Should().Be("booking_date_in_past");
        }
    }

    // ── 2.9 one booking per user per date and time slot ─────────────────────────

    [Fact]
    public async Task SecondBooking_AtAnotherLocation_SameSlot_Returns400WithLocationName()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "dup");
        var (locationA, _) = await SeedLocationAsync(_factory, 2, "Alpha Site");
        var (locationB, _) = await SeedLocationAsync(_factory, 2, "Beta Site");
        var date = FutureDate(7);

        (await PostBookingAsync(user.AccessToken, locationA, date)).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostBookingAsync(user.AccessToken, locationB, date);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("booking_duplicate_other_location");
        problem.GetProperty("params").GetProperty("location").GetString().Should().Be("Alpha Site");

        // The other time slot is still free.
        (await PostBookingAsync(user.AccessToken, locationB, date, "Afternoon")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task SecondBooking_AfterCancellingTheFirst_IsAllowed()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "dupcancel");
        var (locationA, _) = await SeedLocationAsync(_factory, 2);
        var (locationB, _) = await SeedLocationAsync(_factory, 2);
        var date = FutureDate(8);

        var first = await PostBookingAsync(user.AccessToken, locationA, date);
        var firstDto = (await first.Content.ReadFromJsonAsync<BookingDto>())!;
        (await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/bookings/{firstDto.Id}", user.AccessToken)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PostBookingAsync(user.AccessToken, locationB, date)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── 3.2 availability semantics ───────────────────────────────────────────────

    [Fact]
    public async Task Availability_BeforeLottery_ReportsDemand_NotOccupancy()
    {
        var owner = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "avail");
        var (locationId, _) = await SeedLocationAsync(_factory, 10);
        var date = FutureDate(9);
        for (var i = 0; i < 3; i++)
        {
            var u = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, $"req{i}");
            await SeedBookingAsync(_factory, u.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Pending);
        }

        var response = await _client.SendAsync(Authed(HttpMethod.Get,
            $"/api/locations/{locationId}/availability?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}", owner.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await response.Content.ReadFromJsonAsync<List<AvailabilityDto>>())!;

        var morning = rows.Single(r => r.TimeSlot == "Morning");
        morning.AvailableSlots.Should().Be(10);
        morning.TotalSlots.Should().Be(10);
        morning.BookingCount.Should().Be(0);
        morning.PendingCount.Should().Be(3);
        morning.LotteryRan.Should().BeFalse();
    }

    [Fact]
    public async Task Availability_AfterLottery_CountsHeldSlotsAndWaitlist()
    {
        var owner = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "avail2");
        var w1 = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "w1");
        var w2 = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "w2");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 2);
        var date = FutureDate(10);
        await SeedLotteryRunAsync(_factory, locationId, date, TimeSlot.Afternoon);
        await SeedBookingAsync(_factory, w1.User.Id, locationId, slotIds[0], date, TimeSlot.Afternoon, BookingStatus.Won);
        await SeedBookingAsync(_factory, w2.User.Id, locationId, null, date, TimeSlot.Afternoon, BookingStatus.Lost);

        var response = await _client.SendAsync(Authed(HttpMethod.Get,
            $"/api/locations/{locationId}/availability?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}", owner.AccessToken));
        var rows = (await response.Content.ReadFromJsonAsync<List<AvailabilityDto>>())!;

        var afternoon = rows.Single(r => r.TimeSlot == "Afternoon");
        afternoon.AvailableSlots.Should().Be(1);
        afternoon.BookingCount.Should().Be(1);
        afternoon.WaitlistCount.Should().Be(1);
        afternoon.LotteryRan.Should().BeTrue();
    }

    // ── 2.1 same-day booking ─────────────────────────────────────────────────────

    [Fact]
    public async Task SameDay_BeforeSlotEnd_AssignsDirectly_EvenWithoutLotteryRow()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "sameday");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);

        // 2026-06-10 08:00 UTC = 10:00 Berlin → Morning (ends 12:00) still open.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero)))
        {
            var response = await PostBookingAsync(user.AccessToken, locationId, new DateOnly(2026, 6, 10));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = (await response.Content.ReadFromJsonAsync<BookingDto>())!;
            dto.Status.Should().Be("Confirmed");
            dto.ParkingSlotId.Should().Be(slotIds[0]);
        }
    }

    [Fact]
    public async Task SameDay_AfterSlotEnd_Returns400_ButOtherSlotStillOpen()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "sameday2");
        var (locationId, _) = await SeedLocationAsync(_factory, 1);

        // 2026-06-10 11:00 UTC = 13:00 Berlin → Morning closed, Afternoon (ends 18:00) open.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.Zero)))
        {
            var morning = await PostBookingAsync(user.AccessToken, locationId, new DateOnly(2026, 6, 10), "Morning");
            morning.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(morning)).Should().Be("booking_same_day_closed");

            var afternoon = await PostBookingAsync(user.AccessToken, locationId, new DateOnly(2026, 6, 10), "Afternoon");
            afternoon.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task SameDay_WhenFull_Returns400_AndPersistsNothing()
    {
        var first = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "full1");
        var second = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "full2");
        var (locationId, _) = await SeedLocationAsync(_factory, 1);

        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 11, 8, 0, 0, TimeSpan.Zero)))
        {
            (await PostBookingAsync(first.AccessToken, locationId, new DateOnly(2026, 6, 11))).StatusCode.Should().Be(HttpStatusCode.Created);

            var full = await PostBookingAsync(second.AccessToken, locationId, new DateOnly(2026, 6, 11));
            full.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(full)).Should().Be("no_slots_today");
        }

        await WithDbAsync(_factory, async db =>
        {
            var rows = db.Bookings.Where(b => b.UserId == second.User.Id).ToList();
            rows.Should().BeEmpty();
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task WeekBooking_SkipsToday()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "weektoday");
        var (locationId, _) = await SeedLocationAsync(_factory, 3);

        // 2026-06-08 is a Monday; 06:00 UTC = 08:00 Berlin.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 8, 6, 0, 0, TimeSpan.Zero)))
        {
            var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/bookings/week", user.AccessToken, new
            {
                locationId, weekStartDate = "2026-06-08", timeSlot = "Morning"
            }));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("createdBookings").GetArrayLength().Should().Be(4);
            body.GetProperty("skippedDays").GetArrayLength().Should().Be(1);
            body.GetProperty("skippedDays")[0].GetProperty("date").GetString().Should().Be("2026-06-08");
        }
    }

    // ── §5 backfill: confirm ─────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_WonBooking_BeforeDeadline_Succeeds()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "confirm");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = new DateOnly(2026, 6, 12);
        var bookingId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Won);

        // Deadline is 06:00 Berlin = 04:00 UTC on the day; 22:30 UTC the evening before is fine.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 11, 22, 30, 0, TimeSpan.Zero)))
        {
            var response = await _client.SendAsync(Authed(HttpMethod.Post, $"/api/bookings/{bookingId}/confirm", user.AccessToken));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = (await response.Content.ReadFromJsonAsync<BookingDto>())!;
            dto.Status.Should().Be("Confirmed");
        }

        (await GetBookingAsync(_factory, bookingId)).ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Confirm_WonBooking_AfterDeadline_Returns400()
    {
        var user = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "confirmlate");
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = new DateOnly(2026, 6, 12);
        var bookingId = await SeedBookingAsync(_factory, user.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Won);

        // 04:01 UTC = 06:01 Berlin → one minute past the Morning deadline.
        using (_factory.Clock.Pin(new DateTimeOffset(2026, 6, 12, 4, 1, 0, TimeSpan.Zero)))
        {
            var response = await _client.SendAsync(Authed(HttpMethod.Post, $"/api/bookings/{bookingId}/confirm", user.AccessToken));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(response)).Should().Be("confirmation_deadline_passed");
        }

        (await GetBookingAsync(_factory, bookingId)).Status.Should().Be(BookingStatus.Won);
    }
}
