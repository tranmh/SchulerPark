namespace SchulerPark.Tests.Services;

using Microsoft.Extensions.Logging;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Tests.TestSupport;
using Xunit;

// Bug #14: user-/admin-controlled fields must be HTML-encoded in email bodies.
public class EmailServiceHtmlEncodingTests
{
    [Fact]
    public void Greeting_encodes_display_name()
    {
        var html = EmailService.Greeting("<script>alert('x')</script>");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void BookingDetailsTable_encodes_location_name()
    {
        var booking = new Booking
        {
            Location = new Location { Name = "<img src=x onerror=alert(1)>" },
            Date = new DateOnly(2026, 1, 1),
            TimeSlot = TimeSlot.Morning,
        };

        var html = EmailService.BookingDetailsTable(booking);

        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }
}

/// <summary>
/// The mails as MailKit really hands them over, captured by an in-process loopback SMTP
/// server (TestSupport/LoopbackSmtpServer): envelope, subject, encoded user input, links,
/// deadlines and slot numbers in the body, and SMTP failures that are logged, not thrown
/// (B44, B45, B46).
/// </summary>
public class EmailServiceSmtpDeliveryTests
{
    private static readonly DateOnly Date = new(2026, 3, 3);

    private static Booking BookingFor(string language, string? slotNumber = null, TimeSlot timeSlot = TimeSlot.Morning,
        Action<Booking>? configure = null)
    {
        var user = TestData.User(u => { u.PreferredLanguage = language; u.SetName("Max", "Muster"); });
        var location = TestData.Location(l => l.Name = "Göppingen");
        var slot = slotNumber is null ? null : TestData.Slot(location, slotNumber);
        return TestData.Booking(user, location, Date, timeSlot, BookingStatus.Won, slot, configure);
    }

    private static (EmailService Service, CapturingLogger<EmailService> Logger) ServiceFor(LoopbackSmtpServer smtp,
        Action<SmtpSettings>? smtpConfigure = null, Action<BookingSettings>? bookingConfigure = null)
    {
        var logger = new CapturingLogger<EmailService>();
        return (new EmailService(smtp.Settings(smtpConfigure), TestSettings.Booking(bookingConfigure), logger), logger);
    }

    [Fact]
    public async Task SendBookingCreatedAsync_Delivered_SetsEnvelopeSubjectAndHtmlBody()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, logger) = ServiceFor(smtp, bookingConfigure: b => b.LotteryTime = "20:30");
        var booking = BookingFor("en");

        await service.SendBookingCreatedAsync(booking);

        var message = smtp.Single();
        var envelope = Assert.Single(smtp.Envelopes);
        Assert.Equal("noreply@louise.test", envelope.From);
        Assert.Equal(new[] { booking.User.Email }, envelope.To);
        Assert.Equal("LouisE", message.From.Mailboxes.Single().Name);
        Assert.Equal("noreply@louise.test", message.From.Mailboxes.Single().Address);
        Assert.Equal(booking.User.Email, message.To.Mailboxes.Single().Address);
        Assert.Equal("Booking Received — Göppingen on 03.03.2026", message.Subject);
        Assert.Null(message.TextBody);
        var html = message.HtmlBody;
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<p>Hi Max Muster,</p>", html);
        Assert.Contains("The lottery runs at 20:30 the day before", html);
        Assert.Contains("This is an automated message from LouisE", html);
        Assert.Contains(logger.Messages(LogLevel.Information), m => m.Contains("Email sent to " + booking.User.Email));
        Assert.Empty(logger.Messages(LogLevel.Error));
    }

    [Fact]
    public async Task SendLotteryWonAsync_German_ShowsSlotNumberAndBerlinDeadline()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);
        // 06:00 UTC on 3 March is 07:00 in Berlin (CET).
        var booking = BookingFor("de", "A-7",
            configure: b => b.ConfirmationDeadline = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc));

        await service.SendLotteryWonAsync(booking);

        var message = smtp.Single();
        Assert.Equal("Gewonnen! — Göppingen am 03.03.2026", message.Subject);
        var html = message.HtmlBody;
        Assert.Contains("<html lang=\"de\">", html);
        Assert.Contains("<p>Hallo Max Muster,</p>", html);
        Assert.Contains("<strong>Zugewiesener Platz:</strong> A-7", html);
        Assert.Contains("Bitte bestätigen Sie bis 07:00 Uhr am 03.03.2026", html);
        Assert.Contains("Vormittag", html);
        Assert.Contains("Bitte antworten Sie nicht auf diese E-Mail", html);
        Assert.DoesNotContain("Please", html);
    }

    [Fact]
    public async Task SendLotteryWonAsync_NoSlotAndNoStoredDeadline_FallsBackToPlaceholderAndSlotEnd()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendLotteryWonAsync(BookingFor("en"));

        var html = smtp.Single().HtmlBody;
        Assert.Contains("<strong>Assigned Slot:</strong> TBD", html);
        Assert.Contains("Please confirm your usage before 12:00 on 03.03.2026", html);
    }

    [Fact]
    public async Task SendBookingExpiredAsync_TellsTheUserTheSpotExpired_NotToConfirm()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendBookingExpiredAsync(BookingFor("en", "A-1"));

        var message = smtp.Single();
        Assert.Equal("Booking Expired — Göppingen on 03.03.2026", message.Subject);
        var html = message.HtmlBody;
        Assert.Contains("has expired", html);
        Assert.Contains("passed on to the waitlist", html);
        Assert.DoesNotContain("Please confirm", html);
        Assert.DoesNotContain("confirm your booking", html);
    }

    [Fact]
    public async Task SendBookingCancelledByAdminAsync_ReasonWithMarkup_IsTrimmedAndEncoded()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendBookingCancelledByAdminAsync(BookingFor("en"), "  <b>Event</b> & Co  ");

        var html = smtp.Single().HtmlBody;
        Assert.Contains("<p><strong>Reason:</strong> &lt;b&gt;Event&lt;/b&gt; &amp; Co</p>", html);
        Assert.DoesNotContain("<b>Event</b>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendBookingCancelledByAdminAsync_BlankReason_OmitsReasonRow(string? reason)
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendBookingCancelledByAdminAsync(BookingFor("de"), reason);

        var html = smtp.Single().HtmlBody;
        Assert.Contains("Buchung von der Verwaltung storniert", html);
        Assert.DoesNotContain("Grund:", html);
        Assert.DoesNotContain("Reason:", html);
    }

    [Fact]
    public async Task SendSlotReassignedAsync_NamesOldAndNewSlot()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendSlotReassignedAsync(BookingFor("en", "A-2"), "A-1");

        var message = smtp.Single();
        Assert.Equal("New parking slot: A-2 — Göppingen on 03.03.2026", message.Subject);
        Assert.Contains("Slot <strong>A-1</strong> is no longer available", message.HtmlBody);
        Assert.Contains("<strong>New slot:</strong> A-2", message.HtmlBody);
    }

    [Fact]
    public async Task SendEmailVerificationAsync_PutsLinkInButtonAndFallback_AndEncodesName()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendEmailVerificationAsync("new@external.de", "Pia <Pending>", "https://park.test/verify?token=abc", "en");

        var envelope = Assert.Single(smtp.Envelopes);
        Assert.Equal(new[] { "new@external.de" }, envelope.To);
        var html = smtp.Single().HtmlBody;
        Assert.Contains("href=\"https://park.test/verify?token=abc\"", html);
        Assert.Contains("Or open this link: https://park.test/verify?token=abc", html);
        Assert.Contains("<p>Hi Pia &lt;Pending&gt;,</p>", html);
        Assert.DoesNotContain("<Pending>", html);
    }

    [Fact]
    public async Task SendApprovalRequestToAdminAsync_EncodesPendingUserDetails_AndShowsCompanyOnlyWhenGiven()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendApprovalRequestToAdminAsync("admin@schuler.de", "Ada", "p@external.de", "<b>Pia</b>", "Ext & Co",
            "https://park.test/admin/pending", "de");
        await service.SendApprovalRequestToAdminAsync("admin@schuler.de", "Ada", "p@external.de", "Pia", null,
            "https://park.test/admin/pending", "en");

        var messages = smtp.Messages.ToArray();
        Assert.Equal(2, messages.Length);
        var withCompany = messages[0].HtmlBody;
        Assert.Contains("&lt;b&gt;Pia&lt;/b&gt;", withCompany);
        Assert.DoesNotContain("<b>Pia</b>", withCompany);
        Assert.Contains("Ext &amp; Co", withCompany);
        Assert.Contains(">Firma<", withCompany);
        Assert.Contains("p@external.de", withCompany);
        Assert.Contains("href=\"https://park.test/admin/pending\"", withCompany);
        var withoutCompany = messages[1].HtmlBody;
        Assert.DoesNotContain("Company", withoutCompany);
        Assert.DoesNotContain("Firma", withoutCompany);
    }

    [Fact]
    public async Task SendAdminAlertAsync_EncodesHeadingAndParagraphs_KeepsRawSubject()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendAdminAlertAsync("admin@schuler.de", "Ada", "Lottery <failed>", new[] { "a < b", "c & d" }, "en");

        var message = smtp.Single();
        Assert.Equal("Lottery <failed>", message.Subject);
        var html = message.HtmlBody;
        Assert.Contains("<h2 style=\"color: #b91c1c;\">Lottery &lt;failed&gt;</h2>", html);
        Assert.Contains("<p>a &lt; b</p>", html);
        Assert.Contains("<p>c &amp; d</p>", html);
        Assert.Contains("<p>Hi Ada,</p>", html);
    }

    [Fact]
    public async Task SendAccountLockedAsync_MentionsLockoutMinutesAndResetLink()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, _) = ServiceFor(smtp);

        await service.SendAccountLockedAsync("u@schuler.de", "Max", 15, "https://park.test/forgot-password", "en");

        var message = smtp.Single();
        Assert.Equal("Account temporarily locked — LouisE", message.Subject);
        Assert.Contains("<strong>15 minute(s)</strong>", message.HtmlBody);
        Assert.Contains("href=\"https://park.test/forgot-password\"", message.HtmlBody);
    }

    [Fact]
    public async Task SendBookingCreatedAsync_RecipientRejectedByServer_LogsErrorAndDoesNotThrow()
    {
        await using var smtp = new LoopbackSmtpServer { RejectRecipients = true };
        var (service, logger) = ServiceFor(smtp);
        var booking = BookingFor("en");

        var exception = await Record.ExceptionAsync(() => service.SendBookingCreatedAsync(booking));

        Assert.Null(exception);
        Assert.Empty(smtp.Messages);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(booking.User.Email, error.Message);
        Assert.NotNull(error.Exception);
        Assert.Empty(logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task SendPasswordResetAsync_ServerWithoutAuthButCredentialsConfigured_LogsErrorAndDoesNotThrow()
    {
        await using var smtp = new LoopbackSmtpServer();
        var (service, logger) = ServiceFor(smtp, s => { s.Username = "mailer"; s.Password = "secret"; });

        var exception = await Record.ExceptionAsync(() =>
            service.SendPasswordResetAsync("u@schuler.de", "Max", "https://park.test/reset?t=1", "de"));

        Assert.Null(exception);
        Assert.Empty(smtp.Messages);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception != null && e.Message.Contains("u@schuler.de"));
    }
}
