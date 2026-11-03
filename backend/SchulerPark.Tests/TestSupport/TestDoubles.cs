namespace SchulerPark.Tests.TestSupport;

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

// Shared test doubles and builders for unit tests that build services by hand.
// The recording email/push/waitlist fakes and MutableTimeProvider live in
// Integration/Fakes.cs; this file adds what unit tests need on top.

/// <summary>
/// Logger that keeps every entry. EmailService has no SMTP seam, so this is how tests
/// see what it would have sent: with SMTP unconfigured it logs
/// "SMTP not configured, skipping email to {To}: {Subject}" at Debug level.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IEnumerable<string> Messages(LogLevel level) =>
        Entries.Where(e => e.Level == level).Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Enqueue((logLevel, formatter(state, exception), exception));
}

/// <summary>A clock pinned to one instant; <see cref="Advance"/> moves it forward. Never reads the wall clock.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    /// <summary>Monday 2026-03-02 10:00 UTC, a working day well away from DST changes.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _utcNow = utcNow;

    public FixedTimeProvider() : this(DefaultNow) { }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);

    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;

    public DateOnly Today => DateOnly.FromDateTime(_utcNow.UtcDateTime);
}

/// <summary>Fresh, isolated EF Core in-memory databases.</summary>
public static class TestDb
{
    public static AppDbContext InMemory(string? name = null) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options);
}

/// <summary>Option presets for hand-built services.</summary>
public static class TestSettings
{
    /// <summary>No host: EmailService logs and skips every send.</summary>
    public static IOptions<SmtpSettings> SmtpUnconfigured => Options.Create(new SmtpSettings());

    /// <summary>
    /// A host that refuses connections at once (loopback, port 1), so EmailService's
    /// send-failure path runs without any network.
    /// </summary>
    public static IOptions<SmtpSettings> SmtpUnreachable => Options.Create(new SmtpSettings
    {
        Host = "127.0.0.1",
        Port = 1,
        FromAddress = "noreply@test.local",
        FromName = "Test"
    });

    public static IOptions<BookingSettings> Booking(Action<BookingSettings>? configure = null)
    {
        var settings = new BookingSettings();
        configure?.Invoke(settings);
        return Options.Create(settings);
    }
}

/// <summary>
/// Builders for the main domain types with valid defaults. Each call returns a new
/// entity with a fresh id; override what the test cares about with an object
/// initializer-style callback.
/// </summary>
public static class TestData
{
    public static readonly DateTime CreatedAt = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    public static User User(Action<User>? configure = null)
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            Email = $"user-{id:N}@schuler.de",
            EmailVerified = true,
            Company = "Schuler AG",
            Role = UserRole.User,
            ApprovalStatus = ApprovalStatus.Approved,
            PreferredLanguage = "en",
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        };
        user.SetName("Test", "User");
        configure?.Invoke(user);
        return user;
    }

    public static User Admin(Action<User>? configure = null) => User(u =>
    {
        u.Role = UserRole.Admin;
        u.SetName("Ada", "Admin");
        configure?.Invoke(u);
    });

    public static User PendingUser(Action<User>? configure = null) => User(u =>
    {
        u.ApprovalStatus = ApprovalStatus.Pending;
        u.Company = "External GmbH";
        configure?.Invoke(u);
    });

    public static Location Location(Action<Location>? configure = null)
    {
        var id = Guid.NewGuid();
        var location = new Location
        {
            Id = id,
            Name = $"Loc-{id:N}",
            IsActive = true,
            DefaultAlgorithm = LotteryAlgorithm.PureRandom
        };
        configure?.Invoke(location);
        return location;
    }

    public static ParkingSlot Slot(Location location, string number, Action<ParkingSlot>? configure = null)
    {
        var slot = new ParkingSlot
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            Location = location,
            SlotNumber = number,
            IsActive = true
        };
        configure?.Invoke(slot);
        return slot;
    }

    public static List<ParkingSlot> Slots(Location location, int count) =>
        Enumerable.Range(1, count).Select(i => Slot(location, $"A-{i}")).ToList();

    public static Booking Booking(User user, Location location, DateOnly date,
        TimeSlot timeSlot = TimeSlot.Morning, BookingStatus status = BookingStatus.Pending,
        ParkingSlot? slot = null, Action<Booking>? configure = null)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            LocationId = location.Id,
            Location = location,
            ParkingSlotId = slot?.Id,
            ParkingSlot = slot,
            Date = date,
            TimeSlot = timeSlot,
            Status = status,
            CreatedAt = CreatedAt
        };
        configure?.Invoke(booking);
        return booking;
    }

    public static BlockedDay BlockedDay(Location location, DateOnly date, User blockedBy,
        ParkingSlot? slot = null, string? reason = "Maintenance") => new()
    {
        Id = Guid.NewGuid(),
        LocationId = location.Id,
        ParkingSlotId = slot?.Id,
        Date = date,
        Reason = reason,
        BlockedByUserId = blockedBy.Id,
        CreatedAt = CreatedAt
    };

    /// <summary>A location with <paramref name="slotCount"/> slots and one approved user, saved.</summary>
    public static (User User, Location Location, List<ParkingSlot> Slots) SeedSite(AppDbContext db, int slotCount = 3)
    {
        var user = User();
        var location = Location();
        var slots = Slots(location, slotCount);
        db.Users.Add(user);
        db.Locations.Add(location);
        db.ParkingSlots.AddRange(slots);
        db.SaveChanges();
        return (user, location, slots);
    }
}

/// <summary>
/// Seed data for Postgres integration tests (tables and columns as in the EF
/// migrations; enums stored as strings). Fixed ids, idempotent: run it with
/// ExecuteSqlRawAsync after migrating.
/// </summary>
public static class SeedSql
{
    public static readonly Guid LocationId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid AdminId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid UserId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    public static readonly Guid PendingUserId = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    public const string Basic = """
        INSERT INTO "Locations" ("Id", "Name", "IsActive", "DefaultAlgorithm")
        VALUES ('a0000000-0000-0000-0000-000000000001', 'Seed Site', TRUE, 'PureRandom')
        ON CONFLICT DO NOTHING;

        INSERT INTO "ParkingSlots" ("Id", "LocationId", "SlotNumber", "IsActive")
        VALUES ('c0000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'S-1', TRUE),
               ('c0000000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000001', 'S-2', TRUE)
        ON CONFLICT DO NOTHING;

        INSERT INTO "Users" ("Id", "Email", "DisplayName", "FirstName", "LastName", "Company",
                             "EmailVerified", "AccessFailedCount", "Role", "ApprovalStatus",
                             "PreferredLanguage", "CreatedAt", "UpdatedAt")
        VALUES ('b0000000-0000-0000-0000-000000000001', 'seed-admin@schuler.de', 'Ada Admin', 'Ada', 'Admin', 'Schuler AG',
                TRUE, 0, 'Admin', 'Approved', 'en', '2026-03-01T08:00:00Z', '2026-03-01T08:00:00Z'),
               ('b0000000-0000-0000-0000-000000000002', 'seed-user@schuler.de', 'Uma User', 'Uma', 'User', 'Schuler AG',
                TRUE, 0, 'User', 'Approved', 'en', '2026-03-01T08:00:00Z', '2026-03-01T08:00:00Z'),
               ('b0000000-0000-0000-0000-000000000003', 'seed-pending@external.de', 'Pia Pending', 'Pia', 'Pending', 'External GmbH',
                TRUE, 0, 'User', 'Pending', 'de', '2026-03-01T08:00:00Z', '2026-03-01T08:00:00Z')
        ON CONFLICT DO NOTHING;
        """;
}
