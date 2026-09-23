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
    private readonly BookingSettings _booking;
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

    public EmailService(IOptions<SmtpSettings> smtp, IOptions<BookingSettings> booking, ILogger<EmailService> logger)
    {
        _smtp = smtp.Value;
        _booking = booking.Value;
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
        var lotteryTime = _booking.LotteryTimeOfDay.ToString("HH:mm");
        var subject = de
            ? $"Buchung eingegangen — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Booking Received — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Buchung eingegangen</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihre Parkplatzbuchung wurde angelegt:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Ihre Buchung ist jetzt <strong>ausstehend</strong>. Die Verlosung läuft am Vortag um {lotteryTime} Uhr, danach werden Sie über das Ergebnis informiert.</p>
              """
            : $"""
              <h2>Booking Received</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your parking booking has been placed:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Your booking is now <strong>Pending</strong>. The lottery runs at {lotteryTime} the day before and you will be notified of the result.</p>
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
              <p>Melden Sie sich bei LouisE an und bestätigen Sie Ihre Buchung. Unbestätigte Plätze gehen nach der Frist an die Warteliste, sobald dort jemand wartet.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">You Won a Parking Spot!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Great news! You have been assigned a parking spot:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              {ConfirmBefore(deadline, lang)}
              <p>Log in to LouisE and confirm your booking. After the deadline an unconfirmed slot is passed on to the waitlist if somebody is waiting.</p>
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
              <p>Melden Sie sich bei LouisE an und bestätigen Sie Ihre Buchung. Unbestätigte Plätze gehen nach der Frist an die Warteliste, sobald dort jemand wartet.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">A Parking Spot Has Become Available!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>A spot has become available and you have been automatically assigned:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              {ConfirmBefore(deadline, lang)}
              <p>Log in to LouisE and confirm your booking. After the deadline an unconfirmed slot is passed on to the waitlist if somebody is waiting.</p>
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
        var deadline = BerlinDeadline(booking);
        var subject = de
            ? $"Erinnerung: Parkplatz bestätigen — {booking.Location.Name}"
            : $"Reminder: Confirm Your Parking — {booking.Location.Name}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #d97706;">Erinnerung: Bestätigung ausstehend</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihre Parkplatzbuchung ist noch nicht bestätigt. Um <strong>{deadline:HH:mm} Uhr</strong> geht der Platz an die Warteliste, falls dort jemand wartet:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Bitte melden Sie sich jetzt bei LouisE an und bestätigen Sie Ihre Buchung.</strong></p>
              """
            : $"""
              <h2 style="color: #d97706;">Confirmation Reminder</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your parking booking is not confirmed yet. At <strong>{deadline:HH:mm}</strong> the slot is passed on to the waitlist if somebody is waiting:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Please log in to LouisE and confirm your booking now.</strong></p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    // ---- Phase 20 WP4: confirmation model --------------------------------------------

    public async Task SendWaitlistAutoConfirmedAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Platz frei geworden und bestätigt — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"A Spot Opened Up — Confirmed — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #16a34a;">Ein Parkplatz ist frei geworden — er gehört Ihnen!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Kurzfristig ist ein Platz frei geworden. Weil bis zum Zeitfenster nur noch wenig Zeit bleibt, haben wir ihn Ihnen direkt zugewiesen und bestätigt:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Zugewiesener Platz:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "wird noch bekannt gegeben")}</p>
              <p>Ihre Buchung ist <strong>bestätigt</strong> — es ist keine Bestätigung mehr nötig. Falls Sie den Platz nicht nutzen, stornieren Sie bitte in LouisE, damit die nächste Person auf der Warteliste ihn bekommt.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">A Parking Spot Opened Up — It's Yours!</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>A spot freed up at short notice. With little time left before the slot starts, we assigned and confirmed it for you directly:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              <p>Your booking is <strong>Confirmed</strong> — no confirmation step needed. If you won't use it, please cancel in LouisE so the next person on the waitlist gets it.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendUnconfirmedBookingKeptAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Ihr Parkplatz bleibt Ihnen — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Your Parking Spot Is Still Yours — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #16a34a;">Ihr Parkplatz bleibt Ihnen</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihr gewonnener Parkplatz wurde nicht bis zur Frist bestätigt. Da niemand auf der Warteliste stand, behalten Sie ihn trotzdem:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Zugewiesener Platz:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "wird noch bekannt gegeben")}</p>
              <p>Ihre Buchung ist jetzt <strong>bestätigt</strong>. Falls Sie den Platz nicht nutzen, stornieren Sie bitte in LouisE, damit er für andere frei wird.</p>
              """
            : $"""
              <h2 style="color: #16a34a;">Your Parking Spot Is Still Yours</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your won parking spot was not confirmed before the deadline. Since nobody was on the waitlist, you keep it anyway:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Assigned Slot:</strong> {Enc(booking.ParkingSlot?.SlotNumber ?? "TBD")}</p>
              <p>Your booking is now <strong>Confirmed</strong>. If you won't use the spot, please cancel in LouisE so it frees up for others.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendBookingExpiredAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Buchung verfallen — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Booking Expired — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Buchung verfallen</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihr gewonnener Parkplatz wurde nicht bis zur Frist bestätigt und ist deshalb verfallen:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Der Platz wurde an die Warteliste weitergegeben. Falls Sie an diesem Tag trotzdem parken möchten, können Sie erneut buchen — ist noch ein Platz frei, wird er Ihnen sofort zugewiesen.</p>
              """
            : $"""
              <h2>Booking Expired</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your won parking spot was not confirmed before the deadline and has expired:</p>
              {BookingDetailsTable(booking, lang)}
              <p>The slot has been passed on to the waitlist. If you still need to park that day you can book again — if a slot is free it is assigned to you immediately.</p>
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

    // ---- Phase 20 WP1: capacity changes ---------------------------------------------

    public async Task SendSlotReassignedAsync(Booking booking, string oldSlotNumber)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var newSlot = Enc(booking.ParkingSlot?.SlotNumber ?? "?");
        var subject = de
            ? $"Neuer Parkplatz: {booking.ParkingSlot?.SlotNumber} — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"New parking slot: {booking.ParkingSlot?.SlotNumber} — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Ihr Parkplatz wurde geändert</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Platz <strong>{Enc(oldSlotNumber)}</strong> steht an diesem Tag nicht mehr zur Verfügung (gesperrt oder außer Betrieb). Ihre Buchung bleibt bestehen — Sie parken stattdessen hier:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>Neuer Platz:</strong> {newSlot}</p>
              <p>Es ist nichts weiter zu tun.</p>
              """
            : $"""
              <h2>Your parking slot has changed</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Slot <strong>{Enc(oldSlotNumber)}</strong> is no longer available on this day (blocked or taken out of service). Your booking stands — you park here instead:</p>
              {BookingDetailsTable(booking, lang)}
              <p><strong>New slot:</strong> {newSlot}</p>
              <p>No further action is needed.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendSlotWithdrawnAsync(Booking booking)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var subject = de
            ? $"Parkplatz nicht mehr verfügbar — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Parking slot no longer available — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2 style="color: #d97706;">Ihr Parkplatz ist nicht mehr verfügbar</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Der Ihnen zugewiesene Platz wurde für diesen Tag gesperrt oder außer Betrieb genommen, und es war kein anderer Platz mehr frei:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Ihre Buchung steht jetzt auf der <strong>Warteliste</strong>. Wird ein Platz frei, erhalten Sie ihn automatisch und werden benachrichtigt.</p>
              """
            : $"""
              <h2 style="color: #d97706;">Your parking slot is no longer available</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>The slot assigned to you was blocked or taken out of service for this day, and no other slot was free:</p>
              {BookingDetailsTable(booking, lang)}
              <p>Your booking is now on the <strong>waitlist</strong>. If a slot frees up you get it automatically and will be notified.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendBookingCancelledByAdminAsync(Booking booking, string? reason)
    {
        var lang = LanguageOf(booking);
        var de = Localization.IsGerman(lang);
        var reasonRow = string.IsNullOrWhiteSpace(reason)
            ? ""
            : (de ? $"<p><strong>Grund:</strong> {Enc(reason.Trim())}</p>" : $"<p><strong>Reason:</strong> {Enc(reason.Trim())}</p>");
        var subject = de
            ? $"Buchung storniert — {booking.Location.Name} am {booking.Date:dd.MM.yyyy}"
            : $"Booking Cancelled — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml(lang, de
            ? $"""
              <h2>Buchung von der Verwaltung storniert</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Ihre Parkplatzbuchung wurde von einem Administrator storniert:</p>
              {BookingDetailsTable(booking, lang)}
              {reasonRow}
              <p>Falls Sie an diesem Tag trotzdem parken möchten, buchen Sie bitte einen anderen Standort.</p>
              """
            : $"""
              <h2>Booking cancelled by an administrator</h2>
              {Greeting(booking.User.DisplayName, lang)}
              <p>Your parking booking has been cancelled by an administrator:</p>
              {BookingDetailsTable(booking, lang)}
              {reasonRow}
              <p>If you still need to park that day, please book another location.</p>
              """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendAdminAlertAsync(string adminEmail, string adminDisplayName, string subject, IReadOnlyList<string> paragraphs, string language)
    {
        var content = string.Join("\n", paragraphs.Select(p => $"<p>{Enc(p)}</p>"));
        var body = BuildHtml(language, $"""
            <h2 style="color: #b91c1c;">{Enc(subject)}</h2>
            {Greeting(adminDisplayName, language)}
            {content}
            """);

        await SendEmailAsync(adminEmail, subject, body);
    }

    // ---- Phase 20 WP2: password self-service ----------------------------------------

    public async Task SendPasswordResetAsync(string email, string displayName, string resetLink, string language)
    {
        var de = Localization.IsGerman(language);
        var subject = de
            ? "Passwort zurücksetzen — LouisE"
            : "Reset your password — LouisE";
        var body = BuildHtml(language, de
            ? $"""
              <h2>Passwort zurücksetzen</h2>
              {Greeting(displayName, language)}
              <p>Für Ihr LouisE-Konto wurde ein neues Passwort angefordert. Klicken Sie auf den Button, um ein neues Passwort zu vergeben:</p>
              {Button(resetLink, "Neues Passwort vergeben")}
              {LinkFallback(resetLink, language)}
              <p>Der Link ist 1 Stunde gültig und kann nur einmal verwendet werden. Falls Sie kein neues Passwort angefordert haben, können Sie diese E-Mail ignorieren — Ihr Passwort bleibt unverändert.</p>
              """
            : $"""
              <h2>Reset your password</h2>
              {Greeting(displayName, language)}
              <p>A password reset was requested for your LouisE account. Click the button to choose a new password:</p>
              {Button(resetLink, "Choose a new password")}
              {LinkFallback(resetLink, language)}
              <p>The link is valid for 1 hour and can be used once. If you did not request a reset, you can ignore this email — your password stays unchanged.</p>
              """);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendPasswordResetNotApplicableAsync(string email, string displayName, string loginLink, string language)
    {
        var de = Localization.IsGerman(language);
        var subject = de
            ? "Ihr Konto meldet sich mit Microsoft an — LouisE"
            : "Your account signs in with Microsoft — LouisE";
        var body = BuildHtml(language, de
            ? $"""
              <h2>Kein Passwort zum Zurücksetzen</h2>
              {Greeting(displayName, language)}
              <p>Für dieses Konto wurde ein neues Passwort angefordert. Ihr LouisE-Konto hat jedoch kein eigenes Passwort — Sie melden sich mit Ihrem Microsoft-Konto an.</p>
              {Button(loginLink, "Mit Microsoft anmelden")}
              <p>Falls Sie diese Anfrage nicht gestellt haben, können Sie diese E-Mail ignorieren.</p>
              """
            : $"""
              <h2>No password to reset</h2>
              {Greeting(displayName, language)}
              <p>A password reset was requested for this account, but your LouisE account has no password of its own — you sign in with your Microsoft account.</p>
              {Button(loginLink, "Sign in with Microsoft")}
              <p>If you did not make this request, you can ignore this email.</p>
              """);

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendAccountLockedAsync(string email, string displayName, int lockoutMinutes, string forgotPasswordLink, string language)
    {
        var de = Localization.IsGerman(language);
        var subject = de
            ? "Konto vorübergehend gesperrt — LouisE"
            : "Account temporarily locked — LouisE";
        var body = BuildHtml(language, de
            ? $"""
              <h2 style="color: #d97706;">Konto vorübergehend gesperrt</h2>
              {Greeting(displayName, language)}
              <p>Nach mehreren fehlgeschlagenen Anmeldeversuchen wurde Ihr LouisE-Konto für <strong>{lockoutMinutes} Minute(n)</strong> gesperrt. Danach können Sie sich wieder anmelden.</p>
              <p>Falls Sie Ihr Passwort vergessen haben, können Sie es hier zurücksetzen:</p>
              {Button(forgotPasswordLink, "Passwort zurücksetzen")}
              <p>Waren das nicht Sie? Dann versucht möglicherweise jemand, sich mit Ihrer Adresse anzumelden. Ihr Konto ist weiterhin geschützt; wir empfehlen trotzdem, das Passwort zu ändern.</p>
              """
            : $"""
              <h2 style="color: #d97706;">Account temporarily locked</h2>
              {Greeting(displayName, language)}
              <p>After several failed sign-in attempts your LouisE account has been locked for <strong>{lockoutMinutes} minute(s)</strong>. You can sign in again afterwards.</p>
              <p>If you forgot your password, you can reset it here:</p>
              {Button(forgotPasswordLink, "Reset password")}
              <p>Wasn't you? Someone may be trying to sign in with your address. Your account remains protected; we still recommend changing your password.</p>
              """);

        await SendEmailAsync(email, subject, body);
    }

    // ---- building blocks -------------------------------------------------------------

    private static string LanguageOf(Booking booking) =>
        Localization.Normalize(booking.User?.PreferredLanguage);

    // WP4: the deadline is stored on the booking when it becomes Won; a Won booking without one
    // (should not exist after the backfill) is due at slot end.
    private static DateTime BerlinDeadline(Booking booking) =>
        DeadlineHelper.ToBerlin(booking.ConfirmationDeadline ?? DeadlineHelper.SlotEndUtc(booking.Date, booking.TimeSlot));

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
