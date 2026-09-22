namespace SchulerPark.Infrastructure.Services;

using System.Net.Security;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;

/// <summary>
/// Transactional mails in German or English. Each template is written out in
/// both languages side by side (German first) — no string-table indirection, so
/// a wording change is a one-file edit and the HTML stays readable.
/// </summary>
public class EmailService : IEmailService
{
    private const string ButtonStyle =
        "background: #3f8c9d; color: #ffffff; padding: 10px 20px; border-radius: 8px; text-decoration: none;";

    private readonly SmtpSettings _smtp;
    private readonly ILogger<EmailService> _logger;

    /// <summary>
    /// TLS cipher suites offered on STARTTLS. The Schuler mail gateway
    /// (mgate01.schulergroup.com) accepts only DHE-RSA suites for TLS 1.2, and
    /// .NET on Linux does not offer DHE by default — the handshake then dies with
    /// "sslv3 alert handshake failure" and no mail ever leaves the box. This list is
    /// the usual TLS 1.3 + ECDHE set plus the DHE-RSA AEAD suites. Null on Windows,
    /// where <see cref="CipherSuitesPolicy"/> is unsupported and the OS default is used.
    /// </summary>
    private static readonly CipherSuitesPolicy? SmtpCipherSuites = OperatingSystem.IsWindows()
        ? null
        : new CipherSuitesPolicy(new[]
        {
            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_DHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
        });

    public EmailService(IOptions<SmtpSettings> smtp, ILogger<EmailService> logger)
    {
        _smtp = smtp.Value;
        _logger = logger;
    }

    public async Task SendEmailVerificationAsync(string email, string displayName, string verificationLink, string language)
    {
        var de = Localization.IsGerman(language);
        var subject = de
            ? "E-Mail-Adresse bestätigen — LouisE"
            : "Verify your email address — LouisE";
        var body = BuildHtml(language, de
            ? $"""
              <h2>E-Mail-Adresse bestätigen</h2>
              {Greeting(displayName, language)}
              <p>Vielen Dank für Ihre Registrierung bei LouisE. Bitte bestätigen Sie Ihre E-Mail-Adresse, um Ihr Konto zu aktivieren:</p>
              {Button(verificationLink, "E-Mail bestätigen")}
              {LinkFallback(verificationLink, language)}
              <p>Der Link ist 24 Stunden gültig. Falls Sie dieses Konto nicht erstellt haben, können Sie diese E-Mail ignorieren.</p>
              """
            : $"""
              <h2>Verify Your Email Address</h2>
              {Greeting(displayName, language)}
              <p>Thanks for registering with LouisE. Please confirm your email address to activate your account:</p>
              {Button(verificationLink, "Verify Email")}
              {LinkFallback(verificationLink, language)}
              <p>The link is valid for 24 hours. If you did not create this account, you can ignore this email.</p>
              """);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendBookingCreatedAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Buchung eingegangen — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Booking Received — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Buchung eingegangen</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihre Parkplatzbuchung wurde angelegt:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Ihre Buchung ist jetzt <strong>ausstehend</strong>. Die Verlosung läuft um 22 Uhr, danach werden Sie über das Ergebnis informiert.</p>
              """
            : $"""
              <h2>Booking Received</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your parking booking has been placed:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Your booking is now <strong>Pending</strong>. The lottery will run at 10 PM and you will be notified of the result.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendBookingCancelledAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Buchung storniert — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Booking Cancelled — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Buchung storniert</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihre Parkplatzbuchung wurde storniert:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Sie können jederzeit erneut buchen.</p>
              """
            : $"""
              <h2>Booking Cancelled</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your parking booking has been cancelled:</p>
              {BookingDetailsTable(booking, lang)}
              <p>You can book again anytime.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendLotteryWonAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var deadline = BerlinDeadline(booking);
        var subject = de
            ? $"Gewonnen! — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"You Won! — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #16a34a;">Sie haben einen Parkplatz gewonnen!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Gute Nachrichten: Ihnen wurde ein Parkplatz zugewiesen:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Zugewiesener Platz:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "wird noch bekannt gegeben")}</p>
              {ConfirmBefore(deadline, lang)}
              <p>Melden Sie sich bei LouisE an und bestätigen Sie Ihre Buchung, sonst verfällt sie.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">You Won a Parking Spot!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Great news! You have been assigned a parking spot:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              {ConfirmBefore(deadline, lang)}
              <p>Log in to LouisE and confirm your booking, or it will expire.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendLotteryLostAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Verlosungsergebnis — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Lottery Result — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Verlosungsergebnis</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Leider wurden Sie bei der Parkplatzverlosung dieses Mal nicht ausgewählt:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Die Nachfrage war größer als die Anzahl freier Plätze. Viel Glück beim nächsten Mal!</p>
              """
            : $"""
              <h2>Lottery Result</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Unfortunately, you were not selected in the parking lottery this time:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Demand exceeded available spots. Better luck next time!</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendWaitlistWonAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var deadline = BerlinDeadline(booking);
        var subject = de
            ? $"Ein Platz ist frei geworden — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Great News! A Spot Opened Up — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #16a34a;">Ein Parkplatz ist frei geworden!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ein Platz ist frei geworden und wurde Ihnen automatisch zugewiesen:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Zugewiesener Platz:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "wird noch bekannt gegeben")}</p>
              {ConfirmBefore(deadline, lang)}
              <p>Melden Sie sich bei LouisE an und bestätigen Sie Ihre Buchung, sonst verfällt sie.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">A Parking Spot Has Become Available!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>A spot has become available and you have been automatically assigned:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              {ConfirmBefore(deadline, lang)}
              <p>Log in to LouisE and confirm your booking, or it will expire.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendBookingDirectlyConfirmedAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Platz zugewiesen — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Spot Assigned — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #16a34a;">Ihr Parkplatz ist bestätigt!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Die Verlosung für diesen Tag ist bereits gelaufen und es war noch ein Platz frei — er wurde Ihnen direkt zugewiesen:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Zugewiesener Platz:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "wird noch bekannt gegeben")}</p>
              <p>Ihre Buchung ist <strong>bestätigt</strong> — es ist nichts weiter zu tun.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">Your Parking Spot Is Confirmed!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>The lottery for this day has already run and a spot was still free, so it has been assigned to you directly:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              <p>Your booking is <strong>Confirmed</strong> — no further action needed.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendBookingWaitlistedAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Sie stehen auf der Warteliste — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"You're on the Waitlist — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Sie stehen auf der Warteliste</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Alle Plätze für diesen Tag sind bereits vergeben:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Ihre Buchung steht auf der <strong>Warteliste</strong>. Wird ein Platz frei, erhalten Sie ihn automatisch und werden benachrichtigt.</p>
              """
            : $"""
              <h2>You're on the Waitlist</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>All spots for this day are already taken:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Your booking is on the <strong>waitlist</strong> and a spot will be assigned to you automatically if one becomes free. You will be notified.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendConfirmationReminderAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Erinnerung: Parkplatz bestätigen — {booking.Location.Name}"
            : $"Reminder: Confirm Your Parking — {booking.Location.Name}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #d97706;">Erinnerung: Bestätigung ausstehend</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihre Parkplatzbuchung verfällt in Kürze, weil sie noch nicht bestätigt wurde:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Bitte melden Sie sich jetzt bei LouisE an und bestätigen Sie Ihre Buchung.</strong></p>
              """
            : $"""
              <h2 style="color: #d97706;">Confirmation Reminder</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your parking booking is about to expire because it has not been confirmed:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Please log in to LouisE and confirm your booking now.</strong></p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendApprovalRequestToAdminAsync(string adminEmail, string adminDisplayName, string pendingUserEmail, string pendingUserDisplayName, string approvalLink, string adminLanguage)
    {
        var de = Localization.IsGerman(adminLanguage);
        var subject = de
            ? "Neue externe Registrierung wartet auf Freigabe — LouisE"
            : "New external user awaiting approval — LouisE";
        var details = $"""
            <table style="border-collapse: collapse; margin: 16px 0;">
                <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">Name</td><td style="padding: 4px 0;"><strong>{Enc(pendingUserDisplayName)}</strong></td></tr>
                <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">E-Mail</td><td style="padding: 4px 0;"><strong>{Enc(pendingUserEmail)}</strong></td></tr>
            </table>
            """;
        var body = BuildHtml(adminLanguage, de
            ? $"""
              <h2>Externe Registrierung wartet auf Freigabe</h2>
              {Greeting(adminDisplayName, adminLanguage)}
              <p>Ein Benutzer mit einer externen E-Mail-Adresse hat sich registriert und seine Adresse bestätigt. Das Konto bleibt inaktiv, bis ein Administrator es freigibt:</p>
              {details}
              {Button(approvalLink, "Ausstehende Benutzer prüfen")}
              {LinkFallback(approvalLink, adminLanguage)}
              """
            : $"""
              <h2>External Registration Awaiting Approval</h2>
              {Greeting(adminDisplayName, adminLanguage)}
              <p>A user with an external email address has registered and verified their address. The account stays inactive until an administrator accepts it:</p>
              {details}
              {Button(approvalLink, "Review Pending Users")}
              {LinkFallback(approvalLink, adminLanguage)}
              """);

        await SendEmailAsync(adminEmail, subject, body);
    }

    public async Task SendAccountApprovedAsync(string email, string displayName, string loginLink, string language)
    {
        var de = Localization.IsGerman(language);
        var subject = de
            ? "Ihr Konto wurde freigegeben — LouisE"
            : "Your account has been approved — LouisE";
        var body = BuildHtml(language, de
            ? $"""
              <h2 style="color: #16a34a;">Konto freigegeben</h2>
              {Greeting(displayName, language)}
              <p>Ein Administrator hat Ihr LouisE-Konto freigegeben. Sie können sich jetzt anmelden:</p>
              {Button(loginLink, "Anmelden")}
              {LinkFallback(loginLink, language)}
              """
            : $"""
              <h2 style="color: #16a34a;">Account Approved</h2>
              {Greeting(displayName, language)}
              <p>An administrator has approved your LouisE account. You can sign in now:</p>
              {Button(loginLink, "Sign In")}
              {LinkFallback(loginLink, language)}
              """);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendAccountRejectedAsync(string email, string displayName, string language)
    {
        var de = Localization.IsGerman(language);
        var subject = de
            ? "Ihre Registrierung — LouisE"
            : "Your registration — LouisE";
        var body = BuildHtml(language, de
            ? $"""
              <h2>Registrierung nicht freigegeben</h2>
              {Greeting(displayName, language)}
              <p>Leider wurde Ihre Registrierung für das Parkplatzsystem LouisE von einem Administrator nicht freigegeben.</p>
              <p>Wenn Sie glauben, dass es sich um einen Irrtum handelt, wenden Sie sich bitte an Ihre Standortverwaltung.</p>
              """
            : $"""
              <h2>Registration Not Approved</h2>
              {Greeting(displayName, language)}
              <p>Unfortunately your registration for the LouisE parking system was not approved by an administrator.</p>
              <p>If you believe this is a mistake, please contact your site administration.</p>
              """);

        await SendEmailAsync(email, subject, body);
    }

    // ---- building blocks -------------------------------------------------------------

    private static string LanguageOf(Booking booking) =>
        Localization.Normalize(booking.User?.PreferredLanguage);

    private static DateTime BerlinDeadline(Booking booking)
    {
        var deadline = DeadlineHelper.GetConfirmationDeadline(booking.Date, booking.TimeSlot);
        return TimeZoneInfo.ConvertTimeFromUtc(deadline, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
    }

    // Bug #14: HTML-encode user-/admin-controlled values before interpolating into email bodies.
    internal static string Enc(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    internal static string Greeting(string? displayName, string language = Localization.English) =>
        Localization.IsGerman(language)
            ? $"<p>Hallo {Enc(displayName)},</p>"
            : $"<p>Hi {Enc(displayName)},</p>";

    internal static string BookingDetailsTable(Booking booking, string language = Localization.English)
    {
        var de = Localization.IsGerman(language);
        var (location, date, slot) = de ? ("Standort", "Datum", "Zeitfenster") : ("Location", "Date", "Time Slot");
        return $"""
            <table style="border-collapse: collapse; margin: 16px 0;">
                <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">{location}</td><td style="padding: 4px 0;"><strong>{Enc(booking.Location.Name)}</strong></td></tr>
                <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">{date}</td><td style="padding: 4px 0;"><strong>{booking.Date:dd.MM.yyyy}</strong></td></tr>
                <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">{slot}</td><td style="padding: 4px 0;"><strong>{Localization.TimeSlotLabel(booking.TimeSlot, language)}</strong></td></tr>
            </table>
            """;
    }

    private static string ConfirmBefore(DateTime berlinDeadline, string language) =>
        Localization.IsGerman(language)
            ? $"""<p style="color: #d97706;"><strong>Bitte bestätigen Sie bis {berlinDeadline:HH:mm} Uhr am {berlinDeadline:dd.MM.yyyy}, dass Sie den Platz nutzen.</strong></p>"""
            : $"""<p style="color: #d97706;"><strong>Please confirm your usage before {berlinDeadline:HH:mm} on {berlinDeadline:dd.MM.yyyy}.</strong></p>""";

    private static string Button(string href, string label) => $"""
        <p style="margin: 24px 0;">
            <a href="{href}" style="{ButtonStyle}">{label}</a>
        </p>
        """;

    private static string LinkFallback(string href, string language) =>
        Localization.IsGerman(language)
            ? $"""<p style="font-size: 12px; color: #6b7280;">Oder öffnen Sie diesen Link: {href}</p>"""
            : $"""<p style="font-size: 12px; color: #6b7280;">Or open this link: {href}</p>""";

    private static string BuildHtml(string language, string content)
    {
        var footer = Localization.IsGerman(language)
            ? "Dies ist eine automatische Nachricht von LouisE. Bitte antworten Sie nicht auf diese E-Mail."
            : "This is an automated message from LouisE. Please do not reply.";
        return $"""
            <!DOCTYPE html>
            <html lang="{Localization.Normalize(language)}">
            <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #1f2937; max-width: 600px; margin: 0 auto; padding: 20px;">
                {content}
                <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                <p style="font-size: 12px; color: #9ca3af;">{footer}</p>
            </body>
            </html>
            """;
    }

    private async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        if (!_smtp.IsConfigured)
        {
            _logger.LogDebug("SMTP not configured, skipping email to {To}: {Subject}", to, subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        if (SmtpCipherSuites is not null)
            client.SslCipherSuitesPolicy = SmtpCipherSuites;
        try
        {
            await client.ConnectAsync(_smtp.Host, _smtp.Port, MailKit.Security.SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(_smtp.Username))
                await client.AuthenticateAsync(_smtp.Username, _smtp.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
        }
    }
}
