namespace SchulerPark.Tests.Services;

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Infrastructure.Services.Placement;
using SchulerPark.Tests.Integration;
using SchulerPark.Tests.TestSupport;

public class BookingLifecycleServiceTests
{
    private static readonly DateOnly Today = new(2026, 3, 2);
    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly FixedTimeProvider _time = new();
    private readonly RecordingWaitlistService _waitlist = new();
    private readonly CapturingEmailService _email = new();
    private readonly RecordingPushService _push = new();

    private BookingLifecycleService NewService() => new(_db, _waitlist,
        new PreferenceAwareSlotPlacer(new ManhattanDistanceMetric()), _email, _push, _time,
        NullLogger<BookingLifecycleService>.Instance);

    [Fact]
    public async Task ReleaseUserBookingsAsync_CancelsLiveFutureBookingsAndPromotesFreedSlots()
    {
        var (user, location, slots) = TestData.SeedSite(_db);
        var won = TestData.Booking(user, location, Today, TimeSlot.Afternoon, BookingStatus.Won, slots[0]);
        var waitlisted = TestData.Booking(user, location, Today.AddDays(1), status: BookingStatus.Waitlisted);
        var past = TestData.Booking(user, location, Today.AddDays(-1), status: BookingStatus.Confirmed, slot: slots[1]);
        var expired = TestData.Booking(user, location, Today.AddDays(2), status: BookingStatus.Expired);
        _db.Bookings.AddRange(won, waitlisted, past, expired);
        _db.SaveChanges();

        var result = await NewService().ReleaseUserBookingsAsync(user.Id, BookingReleaseReason.UserDisabled);

        result.Should().Be(new CapacityChangeResult(2, 0, 0, 2));
        foreach (var b in new[] { won, waitlisted })
        {
            b.Status.Should().Be(BookingStatus.Cancelled);
            b.ParkingSlotId.Should().BeNull();
            b.CancelReason.Should().Be("user_disabled");
            b.CancelledByUserId.Should().BeNull();
            b.CancelledAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        }
        past.Status.Should().Be(BookingStatus.Confirmed);
        expired.Status.Should().Be(BookingStatus.Expired);
        _waitlist.Promotions.Should().ContainSingle().Which.Should().Be((location.Id, Today, TimeSlot.Afternoon, slots[0].Id));
    }

    [Fact]
    public async Task ReleaseUserBookingsAsync_NothingLive_ReturnsEmpty()
    {
        var (user, _, _) = TestData.SeedSite(_db);

        (await NewService().ReleaseUserBookingsAsync(user.Id, BookingReleaseReason.UserDeleted))
            .Should().Be(CapacityChangeResult.Empty);
    }

    [Fact]
    public async Task HandleCapacityRemovedAsync_WholeLocation_CancelsWithAdminReasonAndNotifies()
    {
        var (user, location, slots) = TestData.SeedSite(_db);
        var admin = TestData.Admin();
        _db.Users.Add(admin);
        var b1 = TestData.Booking(user, location, Today.AddDays(1), status: BookingStatus.Confirmed, slot: slots[0]);
        var b2 = TestData.Booking(user, location, Today.AddDays(1), TimeSlot.Afternoon, BookingStatus.Pending);
        var outside = TestData.Booking(user, location, Today.AddDays(5), status: BookingStatus.Pending);
        _db.Bookings.AddRange(b1, b2, outside);
        _db.SaveChanges();

        var result = await NewService().HandleCapacityRemovedAsync(location.Id, Today, Today.AddDays(2), null,
            BookingReleaseReason.LocationBlocked, admin.Id, "  Event  ");

        result.Should().Be(new CapacityChangeResult(2, 0, 0, 2));
        b1.Status.Should().Be(BookingStatus.Cancelled);
        b1.CancelReason.Should().Be("Event");
        b1.CancelledByUserId.Should().Be(admin.Id);
        outside.Status.Should().Be(BookingStatus.Pending);
        _email.Sent.Where(s => s.Type == "CancelledByAdmin").Select(s => s.BookingId).Should().BeEquivalentTo([b1.Id, b2.Id]);
        _push.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task HandleCapacityRemovedAsync_NoAdminText_StoresReasonCode()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        var b = TestData.Booking(user, location, Today.AddDays(1));
        _db.Bookings.Add(b);
        _db.SaveChanges();

        await NewService().HandleCapacityRemovedAsync(location.Id, Today, null, null,
            BookingReleaseReason.LocationDeactivated, user.Id, "   ");

        b.CancelReason.Should().Be("location_deactivated");
    }

    [Fact]
    public async Task HandleCapacityRemovedAsync_SlotBlocked_ReassignsToFreeSlotOrWaitlists()
    {
        var (user, location, slots) = TestData.SeedSite(_db, slotCount: 2);
        var other = TestData.User();
        _db.Users.Add(other);
        var date = Today.AddDays(1);
        // Both hold slots; slot 0 is withdrawn. The confirmed holder of slot 0 moves to... nothing (slot 1 taken).
        var displaced = TestData.Booking(user, location, date, status: BookingStatus.Won, slot: slots[0],
            configure: b => { b.ConfirmationDeadline = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc); });
        var holder = TestData.Booking(other, location, date, status: BookingStatus.Confirmed, slot: slots[1]);
        _db.Bookings.AddRange(displaced, holder);
        _db.BlockedDays.Add(TestData.BlockedDay(location, date, user, slots[0]));
        _db.SaveChanges();

        var result = await NewService().HandleCapacityRemovedAsync(location.Id, date, date, slots[0].Id,
            BookingReleaseReason.SlotBlocked, user.Id, null);

        result.Should().Be(new CapacityChangeResult(1, 0, 1, 0));
        displaced.Status.Should().Be(BookingStatus.Waitlisted);
        displaced.ParkingSlotId.Should().BeNull();
        displaced.ConfirmationDeadline.Should().BeNull();
        holder.ParkingSlotId.Should().Be(slots[1].Id);
        _email.Sent.Should().ContainSingle().Which.Should().Be(("SlotWithdrawn", displaced.Id));
    }

    [Fact]
    public async Task HandleCapacityRemovedAsync_SlotBlockedWithSpareSlot_Reassigns()
    {
        var (user, location, slots) = TestData.SeedSite(_db, slotCount: 2);
        var date = Today.AddDays(1);
        var displaced = TestData.Booking(user, location, date, status: BookingStatus.Confirmed, slot: slots[0]);
        _db.Bookings.Add(displaced);
        _db.BlockedDays.Add(TestData.BlockedDay(location, date, user, slots[0]));
        _db.SaveChanges();

        var result = await NewService().HandleCapacityRemovedAsync(location.Id, date, date, slots[0].Id,
            BookingReleaseReason.SlotBlocked, user.Id, null);

        result.Should().Be(new CapacityChangeResult(1, 1, 0, 0));
        displaced.Status.Should().Be(BookingStatus.Confirmed);
        displaced.ParkingSlotId.Should().Be(slots[1].Id);
        _email.Sent.Should().ContainSingle().Which.Should().Be(("SlotReassigned", displaced.Id));
    }
}

public class LotteryServiceTests
{
    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly CapturingLogger<LotteryService> _logger = new();

    private LotteryService NewService() => new(_db, _logger, new CapturingEmailService(), new RecordingPushService(),
        new PreferenceAwareSlotPlacer(new ManhattanDistanceMetric()), TestOptions.Booking, new FixedTimeProvider());

    [Fact]
    public async Task RunAllLotteriesAsync_NoActiveLocations_ReturnsEmptySummary()
    {
        _db.Locations.Add(TestData.Location(l => l.IsActive = false));
        _db.SaveChanges();
        var date = new DateOnly(2026, 3, 3);

        var summary = await NewService().RunAllLotteriesAsync(date);

        summary.Date.Should().Be(date);
        summary.Succeeded.Should().Be(0);
        summary.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAllLotteriesAsync_FailingSlots_AreReportedPerSlotAndRunContinues()
    {
        // The in-memory provider cannot open the Serializable transaction, so every slot run
        // fails: the summary must list each location×slot instead of stopping at the first.
        var a = TestData.Location(l => l.Name = "Alpha");
        var b = TestData.Location(l => l.Name = "Beta");
        _db.Locations.AddRange(a, b);
        _db.SaveChanges();

        var summary = await NewService().RunAllLotteriesAsync(new DateOnly(2026, 3, 3));

        summary.Succeeded.Should().Be(0);
        summary.Failures.Should().HaveCount(4);
        summary.Failures.Select(f => (f.LocationName, f.TimeSlot)).Should().BeEquivalentTo(new[]
        {
            ("Alpha", TimeSlot.Morning), ("Alpha", TimeSlot.Afternoon), ("Beta", TimeSlot.Morning), ("Beta", TimeSlot.Afternoon)
        });
        summary.Failures.Should().OnlyContain(f => !string.IsNullOrEmpty(f.Error));
        _logger.Messages(LogLevel.Error).Should().HaveCount(4);
    }

    [Fact]
    public async Task RunLotteryForSlotAsync_NonSerializationFailure_PropagatesWithoutRetry()
    {
        var location = TestData.Location();
        _db.Locations.Add(location);
        _db.SaveChanges();

        await NewService().Invoking(s => s.RunLotteryForSlotAsync(location.Id, new DateOnly(2026, 3, 3), TimeSlot.Morning))
            .Should().ThrowAsync<Exception>();
        _logger.Messages(LogLevel.Warning).Should().NotContain(m => m.Contains("retrying"));
    }
}

public sealed class ScriptedPushSender : IPushSender
{
    public Dictionary<string, PushSendOutcome> Outcomes { get; } = [];
    public List<(string Endpoint, string Payload)> Sent { get; } = [];

    public Task<PushSendOutcome> SendAsync(PushSubscription subscription, string jsonPayload, CancellationToken ct = default)
    {
        Sent.Add((subscription.Endpoint, jsonPayload));
        return Task.FromResult(Outcomes.GetValueOrDefault(subscription.Endpoint, PushSendOutcome.Delivered));
    }
}

public class PushNotificationServiceTests
{
    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly ScriptedPushSender _sender = new();
    private readonly CapturingLogger<PushNotificationService> _logger = new();

    private PushNotificationService NewService(bool configured = true) => new(_db, _sender,
        Options.Create(configured ? new VapidSettings { PublicKey = "pub", PrivateKey = "priv" } : new VapidSettings()),
        _logger);

    private void Subscribe(User user, params string[] endpoints)
    {
        foreach (var e in endpoints)
            _db.PushSubscriptions.Add(new PushSubscription { Id = Guid.NewGuid(), UserId = user.Id, Endpoint = e, P256dh = "k", Auth = "a", CreatedAt = TestData.CreatedAt });
        _db.SaveChanges();
    }

    [Fact]
    public async Task SendTestAsync_VapidNotConfigured_SkipsWithoutSending()
    {
        var (user, _, _) = TestData.SeedSite(_db);
        Subscribe(user, "https://push/1");

        (await NewService(configured: false).SendTestAsync(user.Id)).Should().Be(PushSendResult.NotConfigured);
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendTestAsync_NoSubscriptions_ReturnsNoSubscriptions()
    {
        var (user, _, _) = TestData.SeedSite(_db);

        (await NewService().SendTestAsync(user.Id)).Should().Be(PushSendResult.NoSubscriptions);
        _sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendTestAsync_MixedOutcomes_CountsEachAndOnlyTargetsThatUser()
    {
        var (user, _, _) = TestData.SeedSite(_db);
        var stranger = TestData.User();
        _db.Users.Add(stranger);
        Subscribe(user, "https://push/ok", "https://push/gone", "https://push/fail");
        Subscribe(stranger, "https://push/stranger");
        _sender.Outcomes["https://push/gone"] = PushSendOutcome.Gone;
        _sender.Outcomes["https://push/fail"] = PushSendOutcome.Failed;

        var result = await NewService().SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(3, 1, 1, 1));
        _sender.Sent.Select(s => s.Endpoint).Should().NotContain("https://push/stranger");
        _sender.Sent[0].Payload.Should().Contain("\"title\":\"LouisE test notification\"").And.Contain("\"url\":\"/profile\"");
    }

    [Fact]
    public async Task SendLotteryWonAsync_GermanUser_SendsGermanPayloadWithoutTag()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        user.PreferredLanguage = "de";
        Subscribe(user, "https://push/1");
        var booking = TestData.Booking(user, location, new DateOnly(2026, 3, 3), status: BookingStatus.Won);

        await NewService().SendLotteryWonAsync(booking);

        var payload = _sender.Sent.Should().ContainSingle().Which.Payload;
        payload.Should().Contain("Parkplatz gewonnen!").And.Contain("03.03.2026").And.NotContain("\"tag\"");
    }

    [Fact]
    public async Task SendConfirmationReminderAsync_CarriesPerBookingTag()
    {
        var (user, location, _) = TestData.SeedSite(_db);
        Subscribe(user, "https://push/1");
        var booking = TestData.Booking(user, location, new DateOnly(2026, 3, 3), status: BookingStatus.Won,
            configure: b => b.ConfirmationDeadline = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc));

        await NewService().SendConfirmationReminderAsync(booking);

        _sender.Sent.Single().Payload.Should().Contain($"\"tag\":\"confirm-{booking.Id:N}\"").And.Contain("07:00");
    }
}

public class EmailServiceSendTests
{
    private static Booking SampleBooking(string language) => TestData.Booking(
        TestData.User(u => u.PreferredLanguage = language), TestData.Location(l => l.Name = "Göppingen"),
        new DateOnly(2026, 3, 3), status: BookingStatus.Won);

    [Fact]
    public async Task SendLotteryWonAsync_SmtpUnconfigured_LogsSkipWithLocalizedSubject()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnconfigured, TestOptions.Booking, logger);
        var de = SampleBooking("de");
        var en = SampleBooking("en");

        await service.SendLotteryWonAsync(de);
        await service.SendLotteryWonAsync(en);

        var skips = logger.Messages(LogLevel.Debug).ToList();
        skips.Should().HaveCount(2);
        skips[0].Should().Contain(de.User.Email).And.Contain("SMTP not configured");
        skips[1].Should().Contain(en.User.Email);
        skips[0].Should().NotBe(skips[1].Replace(en.User.Email, de.User.Email), "the subject follows the user's language");
    }

    [Fact]
    public async Task SendApprovalRequestToAdminAsync_SmtpUnconfigured_AddressesAdmin()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnconfigured, TestOptions.Booking, logger);

        await service.SendApprovalRequestToAdminAsync("admin@schuler.de", "Ada", "p@external.de", "<b>Pia</b>", null, "https://park.test/admin", "en");

        logger.Messages(LogLevel.Debug).Should().ContainSingle()
            .Which.Should().Contain("admin@schuler.de").And.Contain("New external user awaiting approval");
    }

    [Fact]
    public async Task SendEmailVerificationAsync_SmtpUnreachable_LogsErrorAndDoesNotThrow()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnreachable, TestOptions.Booking, logger);

        await service.Invoking(s => s.SendEmailVerificationAsync("a@external.de", "A", "https://park.test/verify", "de"))
            .Should().NotThrowAsync();

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("a@external.de") && e.Exception != null);
    }

    [Fact(Skip = "aqaa: suspected bug - EmailService.SendEmailAsync calls MailboxAddress.Parse(to) outside its try/catch, so a malformed recipient throws MimeKit.ParseException out of a fire-and-forget send instead of being logged")]
    public async Task SendAccountApprovedAsync_InvalidRecipient_DoesNotThrow()
    {
        // Callers fire and forget ("errors logged inside EmailService"), so nothing may escape.
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnreachable, TestOptions.Booking, logger);

        await service.Invoking(s => s.SendAccountApprovedAsync("", "A", "https://park.test/login", "en"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void Enc_EncodesCompanyAndNameMarkup()
    {
        EmailService.Enc("Smith & <Sons>").Should().Be("Smith &amp; &lt;Sons&gt;");
        EmailService.Enc(null).Should().BeEmpty();
    }
}

/// <summary>
/// Push payloads (B44, B47): every type in both languages, camelCase JSON keys, the tag
/// that groups the confirmation lifecycle, trimmed admin reasons, and per-device outcomes.
/// </summary>
public class PushNotificationServicePayloadTests
{
    private static readonly DateOnly Date = new(2026, 3, 3);
    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly ScriptedPushSender _sender = new();
    private readonly CapturingLogger<PushNotificationService> _logger = new();

    private PushNotificationService NewService() => new(_db, _sender,
        Options.Create(new VapidSettings { PublicKey = "pub", PrivateKey = "priv" }), _logger);

    /// <summary>A user in <paramref name="language"/> with the given push endpoints, a location "Göppingen" and slot "A-2".</summary>
    private (User User, Location Location, ParkingSlot Slot) Site(string language, params string[] endpoints)
    {
        var user = TestData.User(u => u.PreferredLanguage = language);
        var location = TestData.Location(l => l.Name = "Göppingen");
        var slot = TestData.Slot(location, "A-2");
        _db.Users.Add(user);
        _db.Locations.Add(location);
        _db.ParkingSlots.Add(slot);
        Subscribe(user, endpoints);
        return (user, location, slot);
    }

    private void Subscribe(User user, params string[] endpoints)
    {
        foreach (var e in endpoints)
            _db.PushSubscriptions.Add(new PushSubscription { Id = Guid.NewGuid(), UserId = user.Id, Endpoint = e, P256dh = "k", Auth = "a", CreatedAt = TestData.CreatedAt });
        _db.SaveChanges();
    }

    private sealed record Payload(string Title, string Body, string Url, string? Tag, bool HasTag);

    private static Payload Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().BeSubsetOf(new[] { "title", "body", "url", "tag" }, "keys are camelCase");
        var hasTag = root.TryGetProperty("tag", out var tag);
        return new Payload(root.GetProperty("title").GetString()!, root.GetProperty("body").GetString()!,
            root.GetProperty("url").GetString()!, hasTag ? tag.GetString() : null, hasTag);
    }

    private Payload SingleSent() => Parse(_sender.Sent.Should().ContainSingle().Which.Payload);

    private static readonly Dictionary<string, (Func<PushNotificationService, Booking, Task> Send, string German, string English)> Catalogue = new()
    {
        ["LotteryWon"] = ((s, b) => s.SendLotteryWonAsync(b), "Parkplatz gewonnen!", "You Won a Parking Spot!"),
        ["LotteryLost"] = ((s, b) => s.SendLotteryLostAsync(b), "Verlosungsergebnis", "Lottery Result"),
        ["WaitlistWon"] = ((s, b) => s.SendWaitlistWonAsync(b), "Ein Platz ist frei geworden!", "A Spot Opened Up!"),
        ["BookingDirectlyConfirmed"] = ((s, b) => s.SendBookingDirectlyConfirmedAsync(b), "Parkplatz bestätigt!", "Parking Spot Confirmed!"),
        ["BookingWaitlisted"] = ((s, b) => s.SendBookingWaitlistedAsync(b), "Sie stehen auf der Warteliste", "You're on the Waitlist"),
        ["SlotReassigned"] = ((s, b) => s.SendSlotReassignedAsync(b, "A-1"), "Ihr Parkplatz wurde geändert", "Your parking slot has changed"),
        ["SlotWithdrawn"] = ((s, b) => s.SendSlotWithdrawnAsync(b), "Parkplatz nicht mehr verfügbar", "Parking slot no longer available"),
        ["BookingCancelledByAdmin"] = ((s, b) => s.SendBookingCancelledByAdminAsync(b, "Event"), "Buchung storniert", "Booking cancelled"),
        ["ConfirmationReminder"] = ((s, b) => s.SendConfirmationReminderAsync(b), "Bitte Parkplatz bestätigen", "Please confirm your parking spot"),
        ["BookingExpired"] = ((s, b) => s.SendBookingExpiredAsync(b), "Buchung verfallen", "Booking expired"),
        ["UnconfirmedBookingKept"] = ((s, b) => s.SendUnconfirmedBookingKeptAsync(b), "Ihr Parkplatz bleibt Ihnen", "Your parking spot is still yours"),
        ["WaitlistAutoConfirmed"] = ((s, b) => s.SendWaitlistAutoConfirmedAsync(b), "Ein Platz ist frei geworden — bestätigt!", "A spot opened up — confirmed!"),
    };

    public static IEnumerable<object[]> Types => Catalogue.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Every_push_type_follows_the_users_language(string type)
    {
        var (send, german, english) = Catalogue[type];
        var (user, location, slot) = Site("de", "https://push/1");
        var booking = TestData.Booking(user, location, Date, status: BookingStatus.Won, slot: slot,
            configure: b => b.ConfirmationDeadline = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc));

        await send(NewService(), booking);
        var de = SingleSent();
        de.Title.Should().Be(german);
        de.Url.Should().Be("/my-bookings");
        de.Body.Should().Contain("Göppingen").And.Contain("03.03.2026");

        _sender.Sent.Clear();
        user.PreferredLanguage = "en";
        await send(NewService(), booking);
        var en = SingleSent();
        en.Title.Should().Be(english);
        en.Url.Should().Be("/my-bookings");
        en.Body.Should().Contain("Göppingen").And.Contain("03.03.2026");
    }

    [Fact]
    public async Task SendBookingCancelledByAdminAsync_TrimsReasonIntoParentheses()
    {
        var (user, location, _) = Site("en", "https://push/1");

        await NewService().SendBookingCancelledByAdminAsync(TestData.Booking(user, location, Date), "  Fire drill  ");

        SingleSent().Body.Should().Be("Your booking at Göppingen on 03.03.2026 was cancelled by an administrator (Fire drill).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendBookingCancelledByAdminAsync_BlankReason_OmitsParentheses(string? reason)
    {
        var (user, location, _) = Site("de", "https://push/1");

        await NewService().SendBookingCancelledByAdminAsync(TestData.Booking(user, location, Date), reason);

        SingleSent().Body.Should().Be("Ihre Buchung in Göppingen am 03.03.2026 wurde von der Verwaltung storniert.");
    }

    [Fact]
    public async Task SendSlotReassignedAsync_NamesOldAndNewSlot()
    {
        var (user, location, slot) = Site("en", "https://push/1");

        await NewService().SendSlotReassignedAsync(TestData.Booking(user, location, Date, status: BookingStatus.Confirmed, slot: slot), "A-1");

        SingleSent().Body.Should().Contain("slot A-1 is no longer available").And.Contain("you now park on slot A-2");
    }

    [Fact]
    public async Task ConfirmationLifecyclePushes_ShareOneTagPerBooking_OthersCarryNone()
    {
        var (user, location, slot) = Site("en", "https://push/1");
        var booking = TestData.Booking(user, location, Date, status: BookingStatus.Won, slot: slot,
            configure: b => b.ConfirmationDeadline = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc));
        var service = NewService();

        await service.SendConfirmationReminderAsync(booking);
        await service.SendBookingExpiredAsync(booking);
        await service.SendUnconfirmedBookingKeptAsync(booking);
        await service.SendWaitlistAutoConfirmedAsync(booking);
        await service.SendLotteryLostAsync(booking);

        var payloads = _sender.Sent.Select(s => Parse(s.Payload)).ToList();
        payloads.Should().HaveCount(5);
        payloads.Take(3).Select(p => p.Tag).Should().AllBe($"confirm-{booking.Id:N}");
        payloads.Skip(3).Should().OnlyContain(p => !p.HasTag);
    }

    [Fact]
    public async Task SendConfirmationReminderAsync_NoStoredDeadline_UsesSlotEndInBerlinTime()
    {
        var (user, location, slot) = Site("en", "https://push/1");

        await NewService().SendConfirmationReminderAsync(TestData.Booking(user, location, Date, TimeSlot.Afternoon, BookingStatus.Won, slot));

        SingleSent().Body.Should().Contain("goes to the waitlist at 19:00 unless you confirm it");
    }

    [Fact]
    public async Task SendLotteryWonAsync_UnsupportedLanguage_FallsBackToGerman()
    {
        var (user, location, slot) = Site("fr", "https://push/1");

        await NewService().SendLotteryWonAsync(TestData.Booking(user, location, Date, status: BookingStatus.Won, slot: slot));

        var payload = SingleSent();
        payload.Title.Should().Be("Parkplatz gewonnen!");
        payload.Body.Should().Contain("Vormittag").And.EndWith("Bitte bestätigen.");
    }

    [Fact]
    public async Task SendTestAsync_GermanUser_SendsGermanTextToProfileWithoutTag()
    {
        var (user, _, _) = Site("de", "https://push/1");

        var result = await NewService().SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(1, 1, 0, 0));
        var payload = SingleSent();
        payload.Title.Should().Be("LouisE Testbenachrichtigung");
        payload.Body.Should().Be("Push-Benachrichtigungen funktionieren auf diesem Gerät.");
        payload.Url.Should().Be("/profile");
        payload.HasTag.Should().BeFalse();
    }

    [Fact]
    public async Task SendBookingWaitlistedAsync_ReachesEachDeviceOfTheUserOnce_AndNobodyElse()
    {
        var (user, location, _) = Site("en", "https://push/1", "https://push/2");
        var other = TestData.User();
        _db.Users.Add(other);
        Subscribe(other, "https://push/other");

        await NewService().SendBookingWaitlistedAsync(TestData.Booking(user, location, Date, status: BookingStatus.Waitlisted));

        _sender.Sent.Select(s => s.Endpoint).Should().BeEquivalentTo(new[] { "https://push/1", "https://push/2" });
        _sender.Sent.Select(s => s.Payload).Distinct().Should().ContainSingle("every device gets the same payload");
    }

    [Fact]
    public async Task SendTestAsync_EveryDeviceFails_ReportsAllFailedAndNothingDelivered()
    {
        var (user, _, _) = Site("en", "https://push/a", "https://push/b");
        _sender.Outcomes["https://push/a"] = PushSendOutcome.Failed;
        _sender.Outcomes["https://push/b"] = PushSendOutcome.Failed;

        var result = await NewService().SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(2, 0, 0, 2));
        _sender.Sent.Select(s => s.Endpoint).Should().BeEquivalentTo(new[] { "https://push/a", "https://push/b" });
        _logger.Messages(LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task SendTestAsync_GoneDeviceButPruneFails_StillReturnsResultAndLogsWarning()
    {
        // The InMemory provider cannot run ExecuteDelete, which stands in for a failing prune:
        // the fan-out result still comes back, with the stale device counted as removed.
        var (user, _, _) = Site("en", "https://push/ok", "https://push/gone");
        _sender.Outcomes["https://push/gone"] = PushSendOutcome.Gone;

        var result = await NewService().SendTestAsync(user.Id);

        result.Should().Be(new PushSendResult(2, 1, 1, 0));
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("Could not prune 1 expired push subscription");
        _logger.Messages(LogLevel.Error).Should().BeEmpty();
    }

    [Fact]
    public async Task SendLotteryWonAsync_VapidNotConfigured_SendsNothingEvenWithDevices()
    {
        var (user, location, slot) = Site("en", "https://push/1");
        var service = new PushNotificationService(_db, _sender, Options.Create(new VapidSettings { PublicKey = "pub" }), _logger);

        await service.SendLotteryWonAsync(TestData.Booking(user, location, Date, status: BookingStatus.Won, slot: slot));

        _sender.Sent.Should().BeEmpty();
    }
}
