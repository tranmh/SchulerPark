namespace SchulerPark.Tests.TestSupport;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
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

/// <summary>Records every admin fan-out call (lottery failures, watchdog, pending users, feedback); sends nothing.</summary>
public sealed class RecordingAdminNotifier : Core.Interfaces.IAdminNotifier
{
    public ConcurrentQueue<Guid> PendingUsers { get; } = new();
    public ConcurrentQueue<(Core.Models.LotteryRunSummary Summary, int Attempt)> LotteryFailures { get; } = new();
    public ConcurrentQueue<(DateOnly Date, IReadOnlyList<string> Healed, int Swept)> WatchdogInterventions { get; } = new();
    public ConcurrentQueue<Core.Models.FeedbackReport> Feedback { get; } = new();

    public Task PendingUserAsync(User pendingUser) { PendingUsers.Enqueue(pendingUser.Id); return Task.CompletedTask; }
    public Task LotteryFailedAsync(Core.Models.LotteryRunSummary summary, int attempt) { LotteryFailures.Enqueue((summary, attempt)); return Task.CompletedTask; }
    public Task LotteryWatchdogInterventionAsync(DateOnly targetDate, IReadOnlyList<string> healedSlots, int sweptPending)
    { WatchdogInterventions.Enqueue((targetDate, healedSlots, sweptPending)); return Task.CompletedTask; }
    public Task FeedbackReceivedAsync(Core.Models.FeedbackReport report) { Feedback.Enqueue(report); return Task.CompletedTask; }
}

/// <summary>
/// Token service without JWT signing: access tokens are "access:{userId}", refresh tokens
/// "refresh:{n}" kept in memory until revoked. Lets auth-flow tests assert issuance and revocation.
/// </summary>
public sealed class FakeTokenService : Core.Interfaces.ITokenService
{
    private readonly ConcurrentDictionary<string, User> _refresh = new();
    private int _counter;

    public ConcurrentQueue<string> Revoked { get; } = new();

    public string GenerateAccessToken(User user) => $"access:{user.Id}";

    public Task<string> GenerateRefreshTokenAsync(User user, string? ipAddress)
    {
        var token = $"refresh:{Interlocked.Increment(ref _counter)}";
        _refresh[token] = user;
        return Task.FromResult(token);
    }

    public Task<User?> ValidateRefreshTokenAsync(string token) =>
        Task.FromResult(_refresh.TryGetValue(token, out var user) ? user : null);

    public Task RevokeRefreshTokenAsync(string token)
    {
        _refresh.TryRemove(token, out _);
        Revoked.Enqueue(token);
        return Task.CompletedTask;
    }

    public Task RevokeAllUserTokensAsync(Guid userId)
    {
        foreach (var kv in _refresh.Where(kv => kv.Value.Id == userId).ToList())
        {
            _refresh.TryRemove(kv.Key, out _);
            Revoked.Enqueue(kv.Key);
        }
        return Task.CompletedTask;
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

    public static readonly Guid SlotId1 = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid PendingBookingId = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    public static readonly Guid WonBookingId = Guid.Parse("d0000000-0000-0000-0000-000000000002");
    public static readonly Guid BlockedDayId = Guid.Parse("e0000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Builds on <see cref="Basic"/>: a Pending morning booking for the seed user on
    /// 2026-03-03, a Won afternoon booking on slot S-1 with a 07:00 UTC confirmation
    /// deadline, and a whole-location blocked day on 2026-03-04.
    /// </summary>
    public const string BookingsAndBlockedDay = """
        INSERT INTO "Bookings" ("Id", "UserId", "LocationId", "ParkingSlotId", "Date", "TimeSlot", "Status",
                                "CreatedAt", "ConfirmationDeadline")
        VALUES ('d0000000-0000-0000-0000-000000000001', 'b0000000-0000-0000-0000-000000000002',
                'a0000000-0000-0000-0000-000000000001', NULL, '2026-03-03', 'Morning', 'Pending',
                '2026-03-02T08:00:00Z', NULL),
               ('d0000000-0000-0000-0000-000000000002', 'b0000000-0000-0000-0000-000000000002',
                'a0000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000001', '2026-03-03', 'Afternoon', 'Won',
                '2026-03-02T08:00:00Z', '2026-03-03T07:00:00Z')
        ON CONFLICT DO NOTHING;

        INSERT INTO "BlockedDays" ("Id", "LocationId", "ParkingSlotId", "Date", "Reason", "BlockedByUserId", "CreatedAt")
        VALUES ('e0000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', NULL, '2026-03-04',
                'Maintenance', 'b0000000-0000-0000-0000-000000000001', '2026-03-02T08:00:00Z')
        ON CONFLICT DO NOTHING;
        """;
}

/// <summary>
/// Minimal in-process SMTP server on a loopback port (no TLS, no AUTH, nothing beyond
/// 127.0.0.1). It answers EHLO/MAIL/RCPT/DATA/QUIT, parses each delivered message with
/// MimeKit and keeps it in <see cref="Messages"/>, so tests can assert on the subject and
/// HTML body EmailService really hands to MailKit. <see cref="RejectRecipients"/> makes
/// RCPT TO fail with 550, which drives EmailService's send-failure path.
/// </summary>
public sealed class LoopbackSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentBag<Task> _sessions = new();

    public LoopbackSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Every message that completed DATA, in delivery order.</summary>
    public ConcurrentQueue<MimeMessage> Messages { get; } = new();

    /// <summary>Envelope (MAIL FROM / RCPT TO) of each delivered message, in delivery order.</summary>
    public ConcurrentQueue<(string From, IReadOnlyList<string> To)> Envelopes { get; } = new();

    /// <summary>When true every RCPT TO is answered with 550, so MailKit fails before DATA.</summary>
    public bool RejectRecipients { get; set; }

    /// <summary>SMTP settings pointing EmailService at this server (no username, so no AUTH).</summary>
    public IOptions<SmtpSettings> Settings(Action<SmtpSettings>? configure = null)
    {
        var s = new SmtpSettings
        {
            Host = "127.0.0.1",
            Port = Port,
            FromAddress = "noreply@louise.test",
            FromName = "LouisE"
        };
        configure?.Invoke(s);
        return Options.Create(s);
    }

    /// <summary>The one delivered message; throws when there is none or more than one.</summary>
    public MimeMessage Single()
    {
        if (Messages.Count != 1)
            throw new InvalidOperationException($"expected exactly one delivered message, got {Messages.Count}");
        Messages.TryPeek(out var message);
        return message!;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _sessions.Add(HandleSessionAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task HandleSessionAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                // Latin1 is byte-transparent, so the raw message bytes can be rebuilt for MimeKit.
                using var reader = new StreamReader(stream, Encoding.Latin1, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.Latin1, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

                await writer.WriteLineAsync("220 loopback ESMTP ready");
                string? from = null;
                var to = new List<string>();

                while (!_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (line is null) return;
                    var verb = (line.Length >= 4 ? line[..4] : line).ToUpperInvariant();

                    switch (verb)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-loopback");
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "HELO":
                            await writer.WriteLineAsync("250 loopback");
                            break;
                        case "MAIL":
                            from = Address(line);
                            to.Clear();
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "RCPT":
                            if (RejectRecipients)
                            {
                                await writer.WriteLineAsync("550 5.1.1 No such user here");
                            }
                            else
                            {
                                to.Add(Address(line));
                                await writer.WriteLineAsync("250 OK");
                            }
                            break;
                        case "DATA":
                            await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                            var raw = new StringBuilder();
                            while (true)
                            {
                                var dataLine = await reader.ReadLineAsync(_cts.Token);
                                if (dataLine is null) return;
                                if (dataLine == ".") break;
                                // undo SMTP dot-stuffing
                                raw.Append(dataLine.StartsWith('.') ? dataLine[1..] : dataLine).Append("\r\n");
                            }
                            using (var ms = new MemoryStream(Encoding.Latin1.GetBytes(raw.ToString())))
                                Messages.Enqueue(await MimeMessage.LoadAsync(ms, _cts.Token));
                            Envelopes.Enqueue((from ?? "", to.ToList()));
                            await writer.WriteLineAsync("250 OK queued");
                            break;
                        case "RSET":
                        case "NOOP":
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 Bye");
                            return;
                        default:
                            await writer.WriteLineAsync("500 Unrecognised command");
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    private static string Address(string line)
    {
        var open = line.IndexOf('<');
        var close = line.LastIndexOf('>');
        return open >= 0 && close > open ? line[(open + 1)..close] : line[(line.IndexOf(':') + 1)..].Trim();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch { /* shutting down */ }
        try { await Task.WhenAll(_sessions); } catch { /* shutting down */ }
        _cts.Dispose();
    }
}
