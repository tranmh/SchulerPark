using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// The real app (in-process) on the run's PostgreSQL service (AQAA_PG_CONNECTION_STRING).
/// Each fixture creates its own database, migrates it and drops it afterwards.
/// </summary>
public sealed class PgWebApplicationFactory(string connectionString) : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        });
    }
}

public sealed class PgAppFixture : IAsyncLifetime
{
    private string? _adminConnectionString;
    private string? _dbName;

    public bool Available { get; private set; }
    public string SkipReason { get; private set; } = "AQAA_PG_CONNECTION_STRING not set";
    public PgWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("AQAA_PG_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
            return;

        _dbName = $"aqaa_it_{Guid.NewGuid():N}";
        await using (var conn = new NpgsqlConnection(_adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _dbName }.ConnectionString;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options))
            await db.Database.MigrateAsync();

        Factory = new PgWebApplicationFactory(cs);
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (!Available) return;
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>https base address so the Secure refresh cookie round-trips.</summary>
    public HttpClient NewClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
}

[CollectionDefinition("PgApp")]
public class PgAppCollection : ICollectionFixture<PgAppFixture> { }

public static class PgTestHelper
{
    public static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var c)
            ? c.GetString() : null;
    }

    public static async Task<Guid> UserIdAsync(CustomWebApplicationFactory f, string email)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    public static async Task<T> InScopeAsync<T>(CustomWebApplicationFactory f, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = f.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public static async Task<List<Booking>> BookingsAtAsync(CustomWebApplicationFactory f, Guid locationId) =>
        await InScopeAsync(f, sp => sp.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .Where(b => b.LocationId == locationId).ToListAsync());
}

// ── t1 / t2: BookingService through /api/bookings ──────────────────────────
[Collection("PgApp")]
[Trait("Category", "Integration")]
public class PgBookingTests(PgAppFixture fx)
{
    private static readonly DateOnly Date = AdminTestHelper.FutureDate(6);

    private static object Req(Guid? locationId, DateOnly date, string slot = "Morning") =>
        new { locationId, date = date.ToString("yyyy-MM-dd"), timeSlot = slot };

    [SkippableFact]
    public async Task CreateBooking_FutureDateBeforeLottery_IsPending()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 2);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken, Req(locId, Date)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var stored = Assert.Single(await PgTestHelper.BookingsAtAsync(fx.Factory, locId));
        Assert.Equal(BookingStatus.Pending, stored.Status);
        Assert.Equal(user.User.Id, stored.UserId);
        Assert.Null(stored.ParkingSlotId);
    }

    [SkippableFact]
    public async Task CreateBooking_PastDate_Returns400WithCode()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken,
            Req(locId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3))));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("booking_date_in_past", await PgTestHelper.CodeOf(res));
    }

    [SkippableFact]
    public async Task CreateBooking_BeyondMaxDaysAhead_Returns400WithCode()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken,
            Req(locId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40))));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("booking_too_far_ahead", await PgTestHelper.CodeOf(res));
    }

    [SkippableFact]
    public async Task CreateBooking_LocationBlocked_Returns400LocationBlocked()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        await AdminTestHelper.WithDbAsync(fx.Factory, async db =>
        {
            db.BlockedDays.Add(new BlockedDay { Id = Guid.NewGuid(), LocationId = locId, Date = Date, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken, Req(locId, Date)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("location_blocked", await PgTestHelper.CodeOf(res));
    }

    [SkippableFact]
    public async Task CreateBooking_DuplicateAtOtherLocation_Returns400WithLocationParam()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var firstName = $"First-{Guid.NewGuid():N}";
        var (loc1, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1, firstName);
        var (loc2, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);

        var ok = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken, Req(loc1, Date)));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var dup = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken, Req(loc2, Date)));

        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var body = await dup.Content.ReadAsStringAsync();
        Assert.Equal("booking_duplicate_other_location", await PgTestHelper.CodeOf(dup));
        Assert.Contains(firstName, body);
        Assert.Empty(await PgTestHelper.BookingsAtAsync(fx.Factory, loc2));
    }

    [SkippableFact]
    public async Task CreateBooking_NoLocationAndPreferredBlocked_FallsBackToOtherLocation()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (preferred, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        await AdminTestHelper.WithDbAsync(fx.Factory, async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == user.User.Id)).PreferredLocationId = preferred;
            db.BlockedDays.Add(new BlockedDay { Id = Guid.NewGuid(), LocationId = preferred, Date = Date, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        // Other locations from earlier tests are active too; the fallback picks any of them.
        await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken, Req(null, Date)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var stored = await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<AppDbContext>().Bookings
            .AsNoTracking().SingleAsync(b => b.UserId == user.User.Id));
        Assert.NotEqual(preferred, stored.LocationId);
        Assert.Contains("unavailable", await res.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task CreateBooking_ConcurrentDuplicateRequests_LeaveAtMostOneLiveBooking()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 3);

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings", user.AccessToken, Req(locId, Date, "Afternoon"))));
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"unexpected {r.StatusCode}"));
        var live = (await PgTestHelper.BookingsAtAsync(fx.Factory, locId))
            .Where(b => b.Status != BookingStatus.Cancelled).ToList();
        Assert.Single(live);
    }

    [SkippableFact]
    public async Task CreateWeek_NonMonday_Returns400()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var monday = NextMonday();

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings/week", user.AccessToken,
            new { locationId = locId, weekStartDate = monday.AddDays(1).ToString("yyyy-MM-dd"), timeSlot = "Morning" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("week_start_not_monday", await PgTestHelper.CodeOf(res));
    }

    [SkippableFact]
    public async Task CreateWeek_OneDayBlocked_CreatesFourAndSkipsBlockedDay()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var user = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var monday = NextMonday();
        await AdminTestHelper.WithDbAsync(fx.Factory, async db =>
        {
            db.BlockedDays.Add(new BlockedDay { Id = Guid.NewGuid(), LocationId = locId, Date = monday.AddDays(2), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/bookings/week", user.AccessToken,
            new { locationId = locId, weekStartDate = monday.ToString("yyyy-MM-dd"), timeSlot = "Morning" }));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var stored = await PgTestHelper.BookingsAtAsync(fx.Factory, locId);
        Assert.Equal(4, stored.Count);
        Assert.DoesNotContain(stored, b => b.Date == monday.AddDays(2));
    }

    [SkippableFact]
    public async Task Cancel_OtherUsersBooking_Returns403AndKeepsBooking()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var owner = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var other = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, owner.User.Id, locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Won);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/bookings/{id}", other.AccessToken));
        var confirm = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, $"/api/bookings/{id}/confirm", other.AccessToken));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
        Assert.Equal(BookingStatus.Won, (await AdminTestHelper.GetBookingAsync(fx.Factory, id)).Status);
    }

    [SkippableFact]
    public async Task Cancel_WonBookingWithWaitlister_FreesSlotAndPromotesWaitlister()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var owner = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var waiter = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        await AdminTestHelper.SeedLotteryRunAsync(fx.Factory, locId, Date, TimeSlot.Morning);
        var won = await AdminTestHelper.SeedBookingAsync(fx.Factory, owner.User.Id, locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Won);
        var wait = await AdminTestHelper.SeedBookingAsync(fx.Factory, waiter.User.Id, locId, null, Date, TimeSlot.Morning, BookingStatus.Waitlisted);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/bookings/{won}", owner.AccessToken));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var cancelled = await AdminTestHelper.GetBookingAsync(fx.Factory, won);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ParkingSlotId);
        Assert.NotNull(cancelled.CancelledAt);
        var promoted = await AdminTestHelper.GetBookingAsync(fx.Factory, wait);
        Assert.Contains(promoted.Status, new[] { BookingStatus.Won, BookingStatus.Confirmed });
        Assert.Equal(slots[0], promoted.ParkingSlotId);
    }

    [SkippableFact]
    public async Task Cancel_AlreadyCancelled_Returns400NotCancellable()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var owner = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, owner.User.Id, locId, null, Date, TimeSlot.Morning, BookingStatus.Cancelled);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/bookings/{id}", owner.AccessToken));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("booking_not_cancellable", await PgTestHelper.CodeOf(res));
    }

    [SkippableFact]
    public async Task Confirm_WonBooking_BecomesConfirmedWithoutAutoReason()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var client = fx.NewClient();
        var owner = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, client);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, owner.User.Id, locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Won);

        var res = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, $"/api/bookings/{id}/confirm", owner.AccessToken));
        var again = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, $"/api/bookings/{id}/confirm", owner.AccessToken));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var stored = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Confirmed, stored.Status);
        Assert.NotNull(stored.ConfirmedAt);
        Assert.Null(stored.AutoConfirmReason);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("booking_not_confirmable", await PgTestHelper.CodeOf(again));
    }

    private static DateOnly NextMonday()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        return d;
    }
}

// ── t3 / t5 / t10: LotteryService, BookingLifecycleService, WaitlistService ──
[Collection("PgApp")]
[Trait("Category", "Integration")]
public class PgServiceTests(PgAppFixture fx)
{
    private static readonly DateOnly Date = AdminTestHelper.FutureDate(8);

    private async Task<Guid> SeedUserAsync()
    {
        var user = TestData.User(u => u.Email = $"svc-{Guid.NewGuid():N}@schuler.de");
        user.Id = Guid.NewGuid();
        await AdminTestHelper.WithDbAsync(fx.Factory, async db => { db.Users.Add(user); await db.SaveChangesAsync(); });
        return user.Id;
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_MoreBookingsThanSlots_WinnersWonWithDeadlineLosersWaitlisted()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 2);
        for (var i = 0; i < 5; i++)
            await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, null, Date, TimeSlot.Morning, BookingStatus.Pending);

        await PgTestHelper.InScopeAsync(fx.Factory, async sp =>
        {
            await sp.GetRequiredService<ILotteryService>().RunLotteryForSlotAsync(locId, Date, TimeSlot.Morning);
            return 0;
        });

        var bookings = await PgTestHelper.BookingsAtAsync(fx.Factory, locId);
        var won = bookings.Where(b => b.Status == BookingStatus.Won).ToList();
        Assert.Equal(2, won.Count);
        Assert.Equal(3, bookings.Count(b => b.Status == BookingStatus.Waitlisted));
        Assert.All(won, b => Assert.NotNull(b.ConfirmationDeadline));
        Assert.Equal(slots.OrderBy(x => x), won.Select(b => b.ParkingSlotId!.Value).OrderBy(x => x));
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_RunTwice_IsIdempotent()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        for (var i = 0; i < 3; i++)
            await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, null, Date, TimeSlot.Afternoon, BookingStatus.Pending);

        async Task Run() => await PgTestHelper.InScopeAsync(fx.Factory, async sp =>
        {
            await sp.GetRequiredService<ILotteryService>().RunLotteryForSlotAsync(locId, Date, TimeSlot.Afternoon);
            return 0;
        });
        await Run();
        var first = (await PgTestHelper.BookingsAtAsync(fx.Factory, locId)).ToDictionary(b => b.Id, b => (b.Status, b.ParkingSlotId));
        await Run();
        var second = (await PgTestHelper.BookingsAtAsync(fx.Factory, locId)).ToDictionary(b => b.Id, b => (b.Status, b.ParkingSlotId));

        Assert.Equal(first, second);
        var runs = await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<AppDbContext>().LotteryRuns
            .CountAsync(r => r.LocationId == locId && r.Date == Date && r.TimeSlot == TimeSlot.Afternoon));
        Assert.Equal(1, runs);
    }

    [SkippableFact]
    public async Task RunLotteryForSlot_SlotBlocked_ReducesCapacity()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 2);
        await AdminTestHelper.WithDbAsync(fx.Factory, async db =>
        {
            db.BlockedDays.Add(new BlockedDay { Id = Guid.NewGuid(), LocationId = locId, ParkingSlotId = slots[0], Date = Date, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        for (var i = 0; i < 3; i++)
            await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, null, Date, TimeSlot.Morning, BookingStatus.Pending);

        await PgTestHelper.InScopeAsync(fx.Factory, async sp =>
        {
            await sp.GetRequiredService<ILotteryService>().RunLotteryForSlotAsync(locId, Date, TimeSlot.Morning);
            return 0;
        });

        var won = (await PgTestHelper.BookingsAtAsync(fx.Factory, locId)).Where(b => b.Status == BookingStatus.Won).ToList();
        var single = Assert.Single(won);
        Assert.Equal(slots[1], single.ParkingSlotId);
    }

    [SkippableFact]
    public async Task ReleaseUserBookings_UserDisabled_CancelsFutureBookingsAndRecordsReason()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var userId = await SeedUserAsync();
        var waiterId = await SeedUserAsync();
        await AdminTestHelper.SeedLotteryRunAsync(fx.Factory, locId, Date, TimeSlot.Morning);
        var won = await AdminTestHelper.SeedBookingAsync(fx.Factory, userId, locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Won);
        var pending = await AdminTestHelper.SeedBookingAsync(fx.Factory, userId, locId, null, Date.AddDays(1), TimeSlot.Morning, BookingStatus.Pending);
        var wait = await AdminTestHelper.SeedBookingAsync(fx.Factory, waiterId, locId, null, Date, TimeSlot.Morning, BookingStatus.Waitlisted);

        await PgTestHelper.InScopeAsync(fx.Factory, sp =>
            sp.GetRequiredService<IBookingLifecycleService>().ReleaseUserBookingsAsync(userId, BookingReleaseReason.UserDisabled));

        foreach (var id in new[] { won, pending })
        {
            var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
            Assert.Equal(BookingStatus.Cancelled, b.Status);
            Assert.NotNull(b.CancelledAt);
            Assert.False(string.IsNullOrEmpty(b.CancelReason));
            Assert.Null(b.ParkingSlotId);
        }
        var promoted = await AdminTestHelper.GetBookingAsync(fx.Factory, wait);
        Assert.Equal(slots[0], promoted.ParkingSlotId);
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_SlotRemovedWithFreeSlot_ReassignsBooking()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 2);
        var adminId = await SeedUserAsync();
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Confirmed);
        await AdminTestHelper.WithDbAsync(fx.Factory, async db =>
        {
            (await db.ParkingSlots.SingleAsync(s => s.Id == slots[0])).IsActive = false;
            await db.SaveChangesAsync();
        });

        await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<IBookingLifecycleService>()
            .HandleCapacityRemovedAsync(locId, Date, Date, slots[0], BookingReleaseReason.SlotDeactivated, adminId, null));

        var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(slots[1], b.ParkingSlotId);
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_SlotRemovedNoFreeSlot_WaitlistsBooking()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var adminId = await SeedUserAsync();
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Won);

        await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<IBookingLifecycleService>()
            .HandleCapacityRemovedAsync(locId, Date, Date, slots[0], BookingReleaseReason.SlotBlocked, adminId, "repair"));

        var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Waitlisted, b.Status);
        Assert.Null(b.ParkingSlotId);
    }

    [SkippableFact]
    public async Task HandleCapacityRemoved_WholeLocation_CancelsWithAdminAndReason()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var adminId = await SeedUserAsync();
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, slots[0], Date, TimeSlot.Morning, BookingStatus.Confirmed);

        await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<IBookingLifecycleService>()
            .HandleCapacityRemovedAsync(locId, Date, Date, null, BookingReleaseReason.LocationBlocked, adminId, "Event"));

        var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(adminId, b.CancelledByUserId);
        Assert.NotNull(b.CancelledAt);
        Assert.False(string.IsNullOrEmpty(b.CancelReason));
    }

    [SkippableFact]
    public async Task TryPromoteWaitlist_TwoWaitlisters_PromotesOldestToWon()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var older = await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, null, Date, TimeSlot.Morning,
            BookingStatus.Waitlisted, DateTime.UtcNow.AddHours(-5));
        var newer = await AdminTestHelper.SeedBookingAsync(fx.Factory, await SeedUserAsync(), locId, null, Date, TimeSlot.Morning,
            BookingStatus.Waitlisted, DateTime.UtcNow.AddHours(-1));

        await PgTestHelper.InScopeAsync(fx.Factory, async sp =>
        {
            await sp.GetRequiredService<IWaitlistService>().TryPromoteWaitlistAsync(locId, Date, TimeSlot.Morning, slots[0]);
            return 0;
        });

        var o = await AdminTestHelper.GetBookingAsync(fx.Factory, older);
        Assert.Equal(BookingStatus.Won, o.Status); // default deadline is days away (B18)
        Assert.Equal(slots[0], o.ParkingSlotId);
        Assert.NotNull(o.ConfirmationDeadline);
        Assert.Equal(BookingStatus.Waitlisted, (await AdminTestHelper.GetBookingAsync(fx.Factory, newer)).Status);
    }
}

// ── t4 / t7 / t11 / t12: auth, users admin and profile endpoints ─────────────
[Collection("PgApp")]
[Trait("Category", "Integration")]
public class PgAuthAndAccountTests(PgAppFixture fx)
{
    private static string NewEmail(string domain = "schuler.de") => $"a-{Guid.NewGuid():N}@{domain}";

    private static Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        c.PostAsJsonAsync("/api/auth/login", new { email, password });

    [SkippableFact]
    public async Task Register_ThenLoginUnverified_Returns403EmailNotVerified()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var email = NewEmail();

        await AuthTestHelper.RegisterAsync(c, email);
        var res = await Login(c, email, AuthTestHelper.DefaultPassword);

        Assert.NotNull(fx.Factory.Emails.VerificationTokenFor(email)); // B2
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("email_not_verified", await PgTestHelper.CodeOf(res)); // B1
    }

    [SkippableFact]
    public async Task Register_OutsideAutoApprovedDomain_LoginReturns403PendingApproval()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var email = NewEmail("example.org");

        await AuthTestHelper.RegisterAsync(c, email);
        await AuthTestHelper.VerifyEmailAsync(fx.Factory, c, email);
        var res = await Login(c, email, AuthTestHelper.DefaultPassword);
        var wrong = await Login(c, email, "Wrong1234!");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("pending_approval", await PgTestHelper.CodeOf(res)); // B6/B7
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [SkippableFact]
    public async Task Login_FiveWrongPasswords_LocksAccountWith423()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var email = NewEmail();
        await AuthTestHelper.RegisterAsync(c, email);
        await AuthTestHelper.VerifyEmailAsync(fx.Factory, c, email);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, email, "Wrong1234!")).StatusCode);
        var correct = await Login(c, email, AuthTestHelper.DefaultPassword);
        var wrong = await Login(c, email, "Wrong1234!");

        Assert.Equal((HttpStatusCode)423, correct.StatusCode);
        Assert.Equal("account_locked", await PgTestHelper.CodeOf(correct));
        Assert.True(correct.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode); // B11
    }

    [SkippableFact]
    public async Task ForgotPassword_UnknownEmail_Returns202()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var res = await fx.NewClient().PostAsJsonAsync("/api/auth/forgot-password", new { email = NewEmail() });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode); // B3
    }

    [SkippableFact]
    public async Task ResetPassword_ValidToken_ChangesPasswordAndTokenIsSingleUse()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var email = NewEmail();
        await AuthTestHelper.RegisterAsync(c, email);
        await AuthTestHelper.VerifyEmailAsync(fx.Factory, c, email);
        await AuthTestHelper.LoginAsync(c, email); // holds a refresh cookie
        for (var i = 0; i < 3; i++) await Login(c, email, "Wrong1234!");

        Assert.Equal(HttpStatusCode.Accepted, (await c.PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode);
        var token = fx.Factory.Emails.ResetTokenFor(email);
        Assert.NotNull(token);

        var reset = await c.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "NewPass123!" });
        var reuse = await c.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Other1234!" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("reset_token_invalid", await PgTestHelper.CodeOf(reuse));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/auth/refresh", null)).StatusCode); // B4: refresh tokens revoked
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, email, AuthTestHelper.DefaultPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(c, email, "NewPass123!")).StatusCode);
        var user = await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(u => u.Email == email));
        Assert.Equal(0, user.AccessFailedCount);
    }

    [SkippableFact]
    public async Task Refresh_WithCookie_RotatesAndLogoutRevokes()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var auth = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, c);

        var refreshed = await c.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = await refreshed.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));

        var logout = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/auth/logout", body.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(auth.User.Id, body.User.Id);
    }

    [SkippableFact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var res = await fx.NewClient().PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [SkippableFact]
    public async Task ChangePassword_Valid_RevokesOtherSessionsAndReturnsTokenPair()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var a = fx.NewClient();
        var b = fx.NewClient();
        var email = NewEmail();
        var auth = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, a, email);
        await AuthTestHelper.LoginAsync(b, email);

        var res = await a.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/profile/change-password", auth.AccessToken,
            new { currentPassword = AuthTestHelper.DefaultPassword, newPassword = "Changed123!" }));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(string.IsNullOrEmpty((await res.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.PostAsync("/api/auth/refresh", null)).StatusCode); // B5
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [SkippableFact]
    public async Task ChangePassword_WrongCurrent_Returns400PasswordIncorrect()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var auth = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, c);

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/profile/change-password", auth.AccessToken,
            new { currentPassword = "Nope12345!", newPassword = "Changed123!" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("password_incorrect", await PgTestHelper.CodeOf(res));
    }

    [SkippableFact]
    public async Task DataExport_ReturnsProfileAndBookings_ThenDeleteDataRevokesAccess()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var email = NewEmail();
        var auth = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, c, email);
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var bookingId = await AdminTestHelper.SeedBookingAsync(fx.Factory, auth.User.Id, locId, null, AdminTestHelper.FutureDate(4), TimeSlot.Morning, BookingStatus.Pending);

        var export = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/profile/data-export", auth.AccessToken));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var json = await export.Content.ReadAsStringAsync();
        Assert.Contains(email, json);
        Assert.Contains(bookingId.ToString(), json);

        var del = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, "/api/profile/data", auth.AccessToken));
        Assert.True(del.IsSuccessStatusCode, $"delete returned {del.StatusCode}");
        var after = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/auth/me", auth.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [SkippableFact]
    public async Task UsersAdmin_ChangeOwnRole_Returns400()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.SuperAdmin, "sa");

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/users/{admin.User.Id}/role", admin.AccessToken, new { role = "User" }));
        var disable = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/users/{admin.User.Id}/disable", admin.AccessToken));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, disable.StatusCode);
    }

    [SkippableFact]
    public async Task UsersAdmin_PlainUser_CannotChangeRoles()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var user = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.User, "u");
        var target = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.User, "t");

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/users/{target.User.Id}/role", user.AccessToken, new { role = "SuperAdmin" }));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var stored = await PgTestHelper.InScopeAsync(fx.Factory, sp => sp.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == target.User.Id));
        Assert.Equal(UserRole.User, stored.Role);
    }

    [SkippableFact]
    public async Task UsersAdmin_DisableUser_ReleasesBookingsAndBlocksLogin()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.SuperAdmin, "sa");
        var email = NewEmail();
        var target = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, c, email);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, target.User.Id, locId, slots[0], AdminTestHelper.FutureDate(5), TimeSlot.Morning, BookingStatus.Confirmed);

        var disable = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/users/{target.User.Id}/disable", admin.AccessToken));
        var enable = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/users/{target.User.Id}/enable", admin.AccessToken));

        Assert.True(disable.IsSuccessStatusCode, $"disable returned {disable.StatusCode}");
        var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(admin.User.Id, b.CancelledByUserId);
        Assert.True(enable.IsSuccessStatusCode, $"enable returned {enable.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, (await Login(c, email, AuthTestHelper.DefaultPassword)).StatusCode);
    }
}

// ── t6: AdminController locations / slots / blocked days ─────────────────────
[Collection("PgApp")]
[Trait("Category", "Integration")]
public class PgAdminControllerTests(PgAppFixture fx)
{
    [SkippableFact]
    public async Task AdminEndpoints_PlainUser_Returns403()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var user = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.User);

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations", user.AccessToken, new { name = "X" }));
        var anon = await c.GetAsync("/api/admin/locations");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }

    [SkippableFact]
    public async Task CreateLocation_EmptyOrTooLongName_Returns400()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");

        var empty = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations", admin.AccessToken, new { name = " " }));
        var tooLong = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations", admin.AccessToken, new { name = new string('x', 101) }));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [SkippableFact]
    public async Task CreateLocationAndSlot_Valid_PersistsAndListsSlot()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");
        var name = $"Loc-{Guid.NewGuid():N}";

        var loc = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/locations", admin.AccessToken, new { name }));
        Assert.Equal(HttpStatusCode.Created, loc.StatusCode);
        var locId = (await loc.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var slot = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots", admin.AccessToken,
            new { locationId = locId, slotNumber = "A1", label = "near door" }));
        Assert.Equal(HttpStatusCode.Created, slot.StatusCode);

        var list = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, $"/api/admin/slots?locationId={locId}", admin.AccessToken));
        var slots = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, slots.GetArrayLength());
        Assert.Equal("A1", slots[0].GetProperty("slotNumber").GetString());
    }

    [SkippableFact]
    public async Task CreateSlot_UnknownLocation_Returns400()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/slots", admin.AccessToken,
            new { locationId = Guid.NewGuid(), slotNumber = "A1" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [SkippableFact]
    public async Task CreateBlockedDay_PastDate_Returns400()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");
        var (locId, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken,
            new { locationId = locId, date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2).ToString("yyyy-MM-dd") }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [SkippableFact]
    public async Task CreateBlockedDay_SlotOfOtherLocation_Returns400()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");
        var (loc1, _) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var (_, otherSlots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken,
            new { locationId = loc1, parkingSlotId = otherSlots[0], date = AdminTestHelper.FutureDate(5).ToString("yyyy-MM-dd") }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [SkippableFact]
    public async Task CreateBlockedDay_WholeLocation_CancelsBookingsWithReason_ThenRemove()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");
        var owner = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, c);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 1);
        var date = AdminTestHelper.FutureDate(7);
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, owner.User.Id, locId, slots[0], date, TimeSlot.Morning, BookingStatus.Confirmed);

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/admin/blocked-days", admin.AccessToken,
            new { locationId = locId, date = date.ToString("yyyy-MM-dd"), reason = "Company event" }));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(admin.User.Id, b.CancelledByUserId);
        Assert.NotNull(b.CancelledAt);

        var blockedId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("blockedDay").GetProperty("id").GetGuid();
        var del = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/admin/blocked-days/{blockedId}", admin.AccessToken));
        var delAgain = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/admin/blocked-days/{blockedId}", admin.AccessToken));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delAgain.StatusCode);
    }

    [SkippableFact]
    public async Task DeactivateSlot_WithBookingAndFreeSlot_MovesBooking()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");
        var owner = await AuthTestHelper.RegisterVerifiedAsync(fx.Factory, c);
        var (locId, slots) = await AdminTestHelper.SeedLocationAsync(fx.Factory, 2);
        var id = await AdminTestHelper.SeedBookingAsync(fx.Factory, owner.User.Id, locId, slots[0], AdminTestHelper.FutureDate(6), TimeSlot.Afternoon, BookingStatus.Confirmed);

        var res = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/admin/slots/{slots[0]}", admin.AccessToken));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var b = await AdminTestHelper.GetBookingAsync(fx.Factory, id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal(slots[1], b.ParkingSlotId);
    }

    [SkippableFact]
    public async Task UpdateAndDeactivate_UnknownIds_Return404()
    {
        Skip.IfNot(fx.Available, fx.SkipReason);
        var c = fx.NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(fx.Factory, c, UserRole.Admin, "adm");

        var loc = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Delete, $"/api/admin/locations/{Guid.NewGuid()}", admin.AccessToken));
        var slot = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Put, $"/api/admin/slots/{Guid.NewGuid()}", admin.AccessToken,
            new { slotNumber = "Z", isActive = true }));
        var grid = await c.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, $"/api/admin/locations/{Guid.NewGuid()}/grid", admin.AccessToken));

        Assert.Equal(HttpStatusCode.NotFound, loc.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, slot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, grid.StatusCode);
    }
}
