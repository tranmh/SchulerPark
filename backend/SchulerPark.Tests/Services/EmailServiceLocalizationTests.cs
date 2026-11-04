namespace SchulerPark.Tests.Services;

using Microsoft.Extensions.Logging;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Tests.TestSupport;
using Xunit;

// Phase 20: emails follow the user's PreferredLanguage; German is the default.
public class EmailServiceLocalizationTests
{
    private static Booking MorningBooking() => new()
    {
        Location = new Location { Name = "Göppingen" },
        Date = new DateOnly(2026, 10, 5),
        TimeSlot = TimeSlot.Morning,
    };

    [Fact]
    public void BookingDetailsTable_is_German_for_de()
    {
        var html = EmailService.BookingDetailsTable(MorningBooking(), "de");

        Assert.Contains("Standort", html);
        Assert.Contains("Zeitfenster", html);
        Assert.Contains("Vormittag", html);
        Assert.DoesNotContain("Morning", html);
    }

    [Fact]
    public void BookingDetailsTable_is_English_for_en()
    {
        var html = EmailService.BookingDetailsTable(MorningBooking(), "en");

        Assert.Contains("Location", html);
        Assert.Contains("Time Slot", html);
        Assert.Contains("Morning", html);
        Assert.DoesNotContain("Vormittag", html);
    }

    [Fact]
    public void Greeting_uses_formal_German()
    {
        Assert.StartsWith("<p>Hallo ", EmailService.Greeting("Anna", "de"));
        Assert.StartsWith("<p>Hi ", EmailService.Greeting("Anna", "en"));
    }

    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("de", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("EN", "en")]
    [InlineData("fr", "de")]
    public void Normalize_maps_any_tag_onto_a_supported_language(string? input, string expected)
    {
        Assert.Equal(expected, Localization.Normalize(input));
    }

    [Theory]
    [InlineData("de", true)]
    [InlineData("en", true)]
    [InlineData("EN", true)]
    [InlineData("en-GB", false)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void IsSupported_accepts_only_the_two_exact_codes(string? input, bool expected)
    {
        Assert.Equal(expected, Localization.IsSupported(input));
    }

    [Fact]
    public void TimeSlotLabel_translates_both_slots()
    {
        Assert.Equal("Nachmittag", Localization.TimeSlotLabel(TimeSlot.Afternoon, "de"));
        Assert.Equal("Afternoon", Localization.TimeSlotLabel(TimeSlot.Afternoon, "en"));
    }
}

/// <summary>
/// B44: every mail type exists in German and English and follows the recipient's language.
/// With SMTP unconfigured, EmailService logs "SMTP not configured, skipping email to {To}: {Subject}"
/// at Debug level, which is where the subject is read from.
/// </summary>
public class EmailServiceCatalogueTests
{
    private static readonly DateOnly Date = new(2026, 3, 3);

    private static Booking BookingIn(string language)
    {
        var user = TestData.User(u => u.PreferredLanguage = language);
        var location = TestData.Location(l => l.Name = "Göppingen");
        return TestData.Booking(user, location, Date, status: BookingStatus.Won, slot: TestData.Slot(location, "A-1"),
            configure: b => b.ConfirmationDeadline = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc));
    }

    private static readonly Dictionary<string, (Func<EmailService, string, Task> Send, string German, string English)> Catalogue = new()
    {
        ["EmailVerification"] = ((s, l) => s.SendEmailVerificationAsync("u@x.de", "Max", "https://park.test/verify", l), "E-Mail-Adresse bestätigen", "Verify your email address"),
        ["BookingCreated"] = ((s, l) => s.SendBookingCreatedAsync(BookingIn(l)), "Buchung eingegangen", "Booking Received"),
        ["BookingCancelled"] = ((s, l) => s.SendBookingCancelledAsync(BookingIn(l)), "Buchung storniert", "Booking Cancelled"),
        ["LotteryWon"] = ((s, l) => s.SendLotteryWonAsync(BookingIn(l)), "Gewonnen!", "You Won!"),
        ["LotteryLost"] = ((s, l) => s.SendLotteryLostAsync(BookingIn(l)), "Verlosungsergebnis", "Lottery Result"),
        ["WaitlistWon"] = ((s, l) => s.SendWaitlistWonAsync(BookingIn(l)), "Ein Platz ist frei geworden", "A Spot Opened Up"),
        ["BookingDirectlyConfirmed"] = ((s, l) => s.SendBookingDirectlyConfirmedAsync(BookingIn(l)), "Platz zugewiesen", "Spot Assigned"),
        ["BookingWaitlisted"] = ((s, l) => s.SendBookingWaitlistedAsync(BookingIn(l)), "Sie stehen auf der Warteliste", "You're on the Waitlist"),
        ["ConfirmationReminder"] = ((s, l) => s.SendConfirmationReminderAsync(BookingIn(l)), "Erinnerung: Parkplatz bestätigen", "Reminder: Confirm Your Parking"),
        ["WaitlistAutoConfirmed"] = ((s, l) => s.SendWaitlistAutoConfirmedAsync(BookingIn(l)), "Platz frei geworden und bestätigt", "A Spot Opened Up — Confirmed"),
        ["UnconfirmedBookingKept"] = ((s, l) => s.SendUnconfirmedBookingKeptAsync(BookingIn(l)), "Ihr Parkplatz bleibt Ihnen", "Your Parking Spot Is Still Yours"),
        ["BookingExpired"] = ((s, l) => s.SendBookingExpiredAsync(BookingIn(l)), "Buchung verfallen", "Booking Expired"),
        ["SlotReassigned"] = ((s, l) => s.SendSlotReassignedAsync(BookingIn(l), "B-9"), "Neuer Parkplatz", "New parking slot"),
        ["SlotWithdrawn"] = ((s, l) => s.SendSlotWithdrawnAsync(BookingIn(l)), "Parkplatz nicht mehr verfügbar", "Parking slot no longer available"),
        ["BookingCancelledByAdmin"] = ((s, l) => s.SendBookingCancelledByAdminAsync(BookingIn(l), "Event"), "Buchung storniert", "Booking Cancelled"),
        ["ApprovalRequestToAdmin"] = ((s, l) => s.SendApprovalRequestToAdminAsync("admin@x.de", "Ada", "p@ext.de", "Pia", "Ext", "https://park.test/admin", l), "Neue externe Registrierung wartet auf Freigabe", "New external user awaiting approval"),
        ["AccountApproved"] = ((s, l) => s.SendAccountApprovedAsync("u@x.de", "Max", "https://park.test/login", l), "Ihr Konto wurde freigegeben", "Your account has been approved"),
        ["AccountRejected"] = ((s, l) => s.SendAccountRejectedAsync("u@x.de", "Max", l), "Ihre Registrierung", "Your registration"),
        ["PasswordReset"] = ((s, l) => s.SendPasswordResetAsync("u@x.de", "Max", "https://park.test/reset", l), "Passwort zurücksetzen", "Reset your password"),
        ["PasswordResetNotApplicable"] = ((s, l) => s.SendPasswordResetNotApplicableAsync("u@x.de", "Max", "https://park.test/login", l), "Ihr Konto meldet sich mit Microsoft an", "Your account signs in with Microsoft"),
        ["AccountLocked"] = ((s, l) => s.SendAccountLockedAsync("u@x.de", "Max", 15, "https://park.test/forgot", l), "Konto vorübergehend gesperrt", "Account temporarily locked"),
    };

    public static IEnumerable<object[]> Types => Catalogue.Keys.Select(k => new object[] { k });

    private static string SubjectOf(string skipLogMessage)
    {
        // "SMTP not configured, skipping email to {To}: {Subject}" — the address has no ": ".
        var i = skipLogMessage.IndexOf(": ", StringComparison.Ordinal);
        Assert.True(i > 0, $"unexpected skip log: {skipLogMessage}");
        return skipLogMessage[(i + 2)..];
    }

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Every_mail_type_has_a_German_and_an_English_subject(string type)
    {
        var (send, german, english) = Catalogue[type];
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnconfigured, TestSettings.Booking(), logger);

        await send(service, "de");
        await send(service, "en");

        var subjects = logger.Messages(LogLevel.Debug).Select(SubjectOf).ToList();
        Assert.Equal(2, subjects.Count);
        Assert.Contains(german, subjects[0]);
        Assert.DoesNotContain(english, subjects[0]);
        Assert.Contains(english, subjects[1]);
        Assert.DoesNotContain(german, subjects[1]);
        Assert.Empty(logger.Messages(LogLevel.Error));
    }

    [Theory]
    [InlineData("fr", "Gewonnen!")]
    [InlineData("", "Gewonnen!")]
    [InlineData("de-AT", "Gewonnen!")]
    [InlineData("en-US", "You Won!")]
    [InlineData("EN", "You Won!")]
    public async Task Booking_mail_language_normalises_the_users_tag_and_defaults_to_German(string preferred, string expectedFragment)
    {
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnconfigured, TestSettings.Booking(), logger);

        await service.SendLotteryWonAsync(BookingIn(preferred));

        var subject = SubjectOf(Assert.Single(logger.Messages(LogLevel.Debug)));
        Assert.Contains(expectedFragment, subject);
    }

    [Fact]
    public async Task SendAdminAlertAsync_uses_the_given_subject_in_either_language()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnconfigured, TestSettings.Booking(), logger);

        await service.SendAdminAlertAsync("admin@x.de", "Ada", "Verlosung fehlgeschlagen", new[] { "p" }, "de");
        await service.SendAdminAlertAsync("admin@x.de", "Ada", "Lottery failed", new[] { "p" }, "en");

        var subjects = logger.Messages(LogLevel.Debug).Select(SubjectOf).ToList();
        Assert.Equal(new[] { "Verlosung fehlgeschlagen", "Lottery failed" }, subjects);
    }
}
