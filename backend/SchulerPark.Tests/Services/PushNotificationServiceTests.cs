namespace SchulerPark.Tests.Services;

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Tests.Integration;

// Push fan-out on the EF InMemory provider: result counting, language selection and
// the best-effort pruning of expired (410) subscriptions. ExecuteDelete is not supported
// by InMemory, so the prune itself fails here — which exercises the "cleanup failure must
// not break the send" path; the real delete is covered by PushPruningDbTests.
public class PushNotificationServiceTests
{
    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly VapidSettings Configured = new() { PublicKey = "pub", PrivateKey = "priv" };

    private static PushNotificationService NewService(AppDbContext db, RecordingPushSender sender, VapidSettings? vapid = null) => new(
        db,
        sender,
        Options.Create(vapid ?? Configured),
        NullLogger<PushNotificationService>.Instance);

    private static User SeedUser(AppDbContext db, string? language, params string[] endpoints)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@schuler.de",
            DisplayName = "Push User",
            EmailVerified = true,
            PreferredLanguage = language ?? "de"
        };
        db.Users.Add(user);
        foreach (var endpoint in endpoints)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Endpoint = endpoint,
                P256dh = "p256dh",
                Auth = "auth",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }
        db.SaveChanges();
        return user;
    }

    private static Booking BookingFor(User user) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        User = user,
        Location = new Location { Name = "Göppingen" },
        Date = new DateOnly(2026, 10, 5),
        TimeSlot = TimeSlot.Morning,
        ParkingSlot = new ParkingSlot { SlotNumber = "17" },
        ConfirmationDeadline = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc)
    };

    private static JsonElement Payload(RecordingPushSender sender) =>
        JsonDocument.Parse(sender.Sent.Single().Payload).RootElement;

    [Fact]
    public async Task SendTestAsync_VapidNotConfigured_ReturnsNotConfiguredAndSendsNothing()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "de", "https://push.test/a");
        var sender = new RecordingPushSender();

        var result = await NewService(db, sender, new VapidSettings()).SendTestAsync(user.Id);

        result.Should().Be(PushSendResult.NotConfigured);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendTestAsync_NoSubscriptions_ReturnsZeroSubscriptions()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "de");
        var sender = new RecordingPushSender();

        var result = await NewService(db, sender).SendTestAsync(user.Id);

        result.Subscriptions.Should().Be(0);
        result.Delivered.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendTestAsync_UnknownUser_ReturnsZeroSubscriptions()
    {
        await using var db = NewDb();
        var sender = new RecordingPushSender();

        var result = await NewService(db, sender).SendTestAsync(Guid.NewGuid());

        result.Subscriptions.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendTestAsync_OnlyCallersDevices_AreTargeted()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "en", "https://push.test/mine");
        SeedUser(db, "en", "https://push.test/someone-else");
        var sender = new RecordingPushSender();

        var result = await NewService(db, sender).SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(1, 1, 0, 0));
        sender.Sent.Select(s => s.Endpoint).Should().Equal("https://push.test/mine");
    }

    [Fact]
    public async Task SendTestAsync_MixedOutcomes_CountsDeliveredRemovedAndFailed()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "de", "https://push.test/ok", "https://push.test/gone", "https://push.test/down");
        var sender = new RecordingPushSender();
        sender.GoneEndpoints["https://push.test/gone"] = 1;
        sender.FailingEndpoints["https://push.test/down"] = 1;

        var result = await NewService(db, sender).SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(3, 1, 1, 1));
    }

    [Fact]
    public async Task SendTestAsync_AllSubscriptionsExpired_PruneFailureDoesNotThrow()
    {
        // InMemory cannot ExecuteDelete; the service must swallow the cleanup error.
        await using var db = NewDb();
        var user = SeedUser(db, "de", "https://push.test/gone1", "https://push.test/gone2");
        var sender = new RecordingPushSender();
        sender.GoneEndpoints["https://push.test/gone1"] = 1;
        sender.GoneEndpoints["https://push.test/gone2"] = 1;

        var result = await NewService(db, sender).SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(2, 0, 2, 0));
        sender.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("de", "LouisE Testbenachrichtigung")]
    [InlineData("en", "LouisE test notification")]
    [InlineData("EN", "LouisE test notification")]
    [InlineData("fr", "LouisE Testbenachrichtigung")]
    public async Task SendTestAsync_TitleFollowsPreferredLanguage(string language, string expectedTitle)
    {
        await using var db = NewDb();
        var user = SeedUser(db, language, "https://push.test/a");
        var sender = new RecordingPushSender();

        await NewService(db, sender).SendTestAsync(user.Id);

        var payload = Payload(sender);
        payload.GetProperty("title").GetString().Should().Be(expectedTitle);
        payload.GetProperty("url").GetString().Should().Be("/profile");
        payload.TryGetProperty("tag", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SendLotteryWonAsync_English_ContainsLocationDateAndSlotLabel()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "en", "https://push.test/a");
        var sender = new RecordingPushSender();

        await NewService(db, sender).SendLotteryWonAsync(BookingFor(user));

        var payload = Payload(sender);
        payload.GetProperty("title").GetString().Should().Be("You Won a Parking Spot!");
        payload.GetProperty("body").GetString().Should()
            .Contain("Göppingen").And.Contain("05.10.2026").And.Contain("Morning").And.Contain("Please confirm");
        payload.GetProperty("url").GetString().Should().Be("/my-bookings");
    }

    [Fact]
    public async Task SendLotteryWonAsync_UserWithoutLanguage_DefaultsToGerman()
    {
        await using var db = NewDb();
        var user = SeedUser(db, null, "https://push.test/a");
        var sender = new RecordingPushSender();
        var booking = BookingFor(user);
        booking.User = null!;

        await NewService(db, sender).SendLotteryWonAsync(booking);

        var payload = Payload(sender);
        payload.GetProperty("title").GetString().Should().Be("Parkplatz gewonnen!");
        payload.GetProperty("body").GetString().Should().Contain("Vormittag").And.Contain("Bitte bestätigen");
    }

    [Fact]
    public async Task SendConfirmationReminderAsync_UsesBerlinDeadlineAndBookingTag()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "de", "https://push.test/a");
        var sender = new RecordingPushSender();
        var booking = BookingFor(user);

        await NewService(db, sender).SendConfirmationReminderAsync(booking);

        var payload = Payload(sender);
        // 05:00 UTC on 5 Oct 2026 is 07:00 CEST.
        payload.GetProperty("body").GetString().Should().Contain("07:00 Uhr");
        payload.GetProperty("tag").GetString().Should().Be($"confirm-{booking.Id:N}");
    }

    [Fact]
    public async Task SendBookingCancelledByAdminAsync_ReasonIsTrimmedAndAppended()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "en", "https://push.test/a", "https://push.test/b");
        var sender = new RecordingPushSender();

        await NewService(db, sender).SendBookingCancelledByAdminAsync(BookingFor(user), "  site closed  ");

        sender.Sent.Should().HaveCount(2);
        var body = JsonDocument.Parse(sender.Sent.First().Payload).RootElement.GetProperty("body").GetString();
        body.Should().EndWith("by an administrator (site closed).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task SendBookingCancelledByAdminAsync_BlankReason_OmitsParentheses(string? reason)
    {
        await using var db = NewDb();
        var user = SeedUser(db, "en", "https://push.test/a");
        var sender = new RecordingPushSender();

        await NewService(db, sender).SendBookingCancelledByAdminAsync(BookingFor(user), reason);

        Payload(sender).GetProperty("body").GetString().Should().EndWith("by an administrator.");
    }

    [Fact]
    public async Task SendSlotReassignedAsync_MentionsOldAndNewSlot()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "en", "https://push.test/a");
        var sender = new RecordingPushSender();

        await NewService(db, sender).SendSlotReassignedAsync(BookingFor(user), "3");

        Payload(sender).GetProperty("body").GetString().Should()
            .Contain("slot 3 is no longer available").And.Contain("you now park on slot 17");
    }

    [Fact]
    public async Task SendLotteryLostAsync_ExpiredSubscription_DoesNotThrow()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "de", "https://push.test/gone");
        var sender = new RecordingPushSender();
        sender.GoneEndpoints["https://push.test/gone"] = 1;

        var act = () => NewService(db, sender).SendLotteryLostAsync(BookingFor(user));

        await act.Should().NotThrowAsync();
    }
}
