using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class DirectAssignmentTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DirectAssignmentTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static DateOnly FutureDate(int daysAhead = 2) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysAhead));

    private HttpRequestMessage Authed(HttpMethod m, string url, string token, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private async Task<Guid> SeedLocationAsync(int slotCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = $"Direct-{Guid.NewGuid():N}",
            Address = "Somewhere",
            IsActive = true,
            DefaultAlgorithm = LotteryAlgorithm.PureRandom
        };
        db.Locations.Add(location);
        for (var i = 1; i <= slotCount; i++)
        {
            db.ParkingSlots.Add(new ParkingSlot
            {
                Id = Guid.NewGuid(),
                LocationId = location.Id,
                SlotNumber = $"A-{i}",
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
        return location.Id;
    }

    private async Task SeedLotteryRunAsync(Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LotteryRuns.Add(new LotteryRun
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Date = date,
            TimeSlot = timeSlot,
            Algorithm = LotteryAlgorithm.PureRandom,
            RanAt = DateTime.UtcNow,
            TotalBookings = 0,
            AvailableSlots = 0
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<Guid>> GetSlotIdsAsync(Guid locationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ParkingSlots
            .Where(s => s.LocationId == locationId)
            .OrderBy(s => s.SlotNumber)
            .Select(s => s.Id)
            .ToListAsync();
    }

    private async Task SeedOccupyingBookingAsync(Guid userId, Guid locationId, Guid slotId,
        DateOnly date, TimeSlot timeSlot, BookingStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LocationId = locationId,
            ParkingSlotId = slotId,
            Date = date,
            TimeSlot = timeSlot,
            Status = status,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<BookingDto> PostBookingAsync(string token, Guid locationId, DateOnly date,
        string timeSlot = "Morning", HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/bookings", token, new
        {
            locationId,
            date = date.ToString("yyyy-MM-dd"),
            timeSlot
        }));
        response.StatusCode.Should().Be(expected);
        return (await response.Content.ReadFromJsonAsync<BookingDto>())!;
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_WithFreeSlot_ReturnsConfirmedWithSlot()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(2);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Confirmed");
        dto.ParkingSlotId.Should().NotBeNull();
        dto.ParkingSlotNumber.Should().NotBeNullOrEmpty();
        dto.ConfirmedAt.Should().NotBeNull();
        dto.ConfirmationDeadline.Should().BeNull();
    }

    [Fact]
    public async Task CreateBooking_BeforeLottery_StaysPending()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(2);

        var dto = await PostBookingAsync(auth.AccessToken, locationId, FutureDate());

        dto.Status.Should().Be("Pending");
        dto.ParkingSlotId.Should().BeNull();
        dto.ConfirmedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_AssignsPreferredSlot()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(3);
        var slotIds = await GetSlotIdsAsync(locationId);
        var preferred = slotIds[2];
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Id == auth.User.Id);
            user.PreferredLocationId = locationId;
            user.PreferredSlotId = preferred;
            await db.SaveChangesAsync();
        }

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Confirmed");
        dto.ParkingSlotId.Should().Be(preferred);
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_AllSlotsHeld_ReturnsLost()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var occupant = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(2);
        var slotIds = await GetSlotIdsAsync(locationId);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);
        await SeedOccupyingBookingAsync(occupant.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Won);
        await SeedOccupyingBookingAsync(occupant.User.Id, locationId, slotIds[1], date, TimeSlot.Morning, BookingStatus.Confirmed);

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Lost");
        dto.ParkingSlotId.Should().BeNull();
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_WritesNoLotteryHistory()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(1);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        int CountHistories()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return db.LotteryHistories.Count();
        }

        var before = CountHistories();
        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);
        dto.Status.Should().Be("Confirmed");
        CountHistories().Should().Be(before);
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_SkipsBlockedSlot()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(2);
        var slotIds = await GetSlotIdsAsync(locationId);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BlockedDays.Add(new BlockedDay
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                ParkingSlotId = slotIds[0],
                Date = date,
                Reason = "Maintenance",
                BlockedByUserId = auth.User.Id,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Confirmed");
        dto.ParkingSlotId.Should().Be(slotIds[1]);
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_BlockedAndOccupied_ReturnsLost()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var occupant = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(2);
        var slotIds = await GetSlotIdsAsync(locationId);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);
        await SeedOccupyingBookingAsync(occupant.User.Id, locationId, slotIds[1], date, TimeSlot.Morning, BookingStatus.Confirmed);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BlockedDays.Add(new BlockedDay
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                ParkingSlotId = slotIds[0],
                Date = date,
                Reason = "Maintenance",
                BlockedByUserId = auth.User.Id,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Lost");
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_SendsDirectConfirmedEmail_NotCreatedEmail()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(1);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Confirmed");
        var sent = _factory.Emails.Sent.Where(s => s.BookingId == dto.Id).ToList();
        sent.Should().Contain(("DirectlyConfirmed", dto.Id));
        sent.Should().NotContain(("BookingCreated", dto.Id));
    }

    [Fact]
    public async Task CreateBooking_AfterLottery_Full_SendsWaitlistedEmail_NotCreatedEmail()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var occupant = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(1);
        var slotIds = await GetSlotIdsAsync(locationId);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);
        await SeedOccupyingBookingAsync(occupant.User.Id, locationId, slotIds[0], date, TimeSlot.Morning, BookingStatus.Confirmed);

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);

        dto.Status.Should().Be("Lost");
        var sent = _factory.Emails.Sent.Where(s => s.BookingId == dto.Id).ToList();
        sent.Should().Contain(("Waitlisted", dto.Id));
        sent.Should().NotContain(("BookingCreated", dto.Id));
    }

    [Fact]
    public async Task CancelDirectlyConfirmed_PromotesLostWaitlister()
    {
        var userA = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var userB = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(1);
        var date = FutureDate(5);
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        var bookingA = await PostBookingAsync(userA.AccessToken, locationId, date);
        bookingA.Status.Should().Be("Confirmed");

        var bookingB = await PostBookingAsync(userB.AccessToken, locationId, date);
        bookingB.Status.Should().Be("Lost");

        var cancel = await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/bookings/{bookingA.Id}", userA.AccessToken));
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var promoted = await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == bookingB.Id);
        promoted.Status.Should().Be(BookingStatus.Won);
        promoted.ParkingSlotId.Should().Be(bookingA.ParkingSlotId);
    }

    [Fact]
    public async Task WeekBooking_MixedLotteryDays_PerDayOutcomes()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(1);

        // A Monday at least 2 days ahead so all Mon-Fri days are bookable.
        var monday = FutureDate(2);
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(1);
        var wednesday = monday.AddDays(2);
        await SeedLotteryRunAsync(locationId, wednesday, TimeSlot.Morning);

        var response = await _client.SendAsync(Authed(HttpMethod.Post, "/api/bookings/week", auth.AccessToken, new
        {
            locationId,
            weekStartDate = monday.ToString("yyyy-MM-dd"),
            timeSlot = "Morning"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var week = (await response.Content.ReadFromJsonAsync<WeekBookingResponse>())!;

        var wednesdayBooking = week.CreatedBookings.Single(b => b.Date == wednesday);
        wednesdayBooking.Status.Should().Be("Confirmed");
        wednesdayBooking.ParkingSlotId.Should().NotBeNull();
        week.CreatedBookings.Where(b => b.Date != wednesday)
            .Should().OnlyContain(b => b.Status == "Pending");
    }

    [Fact]
    public async Task Availability_AfterDirectAssignment_CountsBooking()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client);
        var locationId = await SeedLocationAsync(2);
        var date = FutureDate();
        await SeedLotteryRunAsync(locationId, date, TimeSlot.Morning);

        var dto = await PostBookingAsync(auth.AccessToken, locationId, date);
        dto.Status.Should().Be("Confirmed");

        var response = await _client.SendAsync(Authed(HttpMethod.Get,
            $"/api/locations/{locationId}/availability?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}",
            auth.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var availability = (await response.Content.ReadFromJsonAsync<List<AvailabilityDto>>())!;

        var morning = availability.Single(a => a.Date == date && a.TimeSlot == "Morning");
        morning.BookingCount.Should().Be(1);
        morning.AvailableSlots.Should().Be(morning.TotalSlots - 1);
    }

    private record BookingDto(
        Guid Id, Guid LocationId, string LocationName, Guid? ParkingSlotId, string? ParkingSlotNumber,
        DateOnly Date, string TimeSlot, string Status, DateTime? ConfirmedAt, DateTime CreatedAt,
        DateTime? ConfirmationDeadline, string? FallbackReason);

    private record WeekBookingResponse(List<BookingDto> CreatedBookings, List<SkippedDay> SkippedDays);
    private record SkippedDay(DateOnly Date, string Reason);
    private record AvailabilityDto(DateOnly Date, string TimeSlot, int AvailableSlots, int TotalSlots, int BookingCount);
}
