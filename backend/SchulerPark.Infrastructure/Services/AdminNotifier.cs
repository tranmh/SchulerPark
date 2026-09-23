namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

/// <summary>
/// Phase 20 WP1: resolves the active Admin/SuperAdmin users and sends each an alert in
/// their own language. Sending is awaited (the callers are background jobs / the
/// verification endpoint), but every failure is logged inside <see cref="EmailService"/>.
/// </summary>
public class AdminNotifier : IAdminNotifier
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly AppSettings _app;
    private readonly ILogger<AdminNotifier> _logger;

    public AdminNotifier(AppDbContext db, IEmailService email, IOptions<AppSettings> app, ILogger<AdminNotifier> logger)
    {
        _db = db;
        _email = email;
        _app = app.Value;
        _logger = logger;
    }

    public async Task PendingUserAsync(User pendingUser)
    {
        var approvalLink = $"{BaseUrl}/admin/approvals";
        foreach (var admin in await ActiveAdminsAsync())
        {
            await _email.SendApprovalRequestToAdminAsync(
                admin.Email, admin.DisplayName, pendingUser.Email, pendingUser.DisplayName, approvalLink, admin.Language);
        }
    }

    public async Task LotteryFailedAsync(LotteryRunSummary summary, int attempt)
    {
        var admins = await ActiveAdminsAsync();
        if (admins.Count == 0)
        {
            _logger.LogWarning("Lottery failed for {Date} but no active admin to notify.", summary.Date);
            return;
        }

        foreach (var admin in admins)
        {
            var de = Localization.IsGerman(admin.Language);
            var subject = de
                ? $"Verlosung für {summary.Date:dd.MM.yyyy} fehlgeschlagen — LouisE"
                : $"Lottery for {summary.Date:dd.MM.yyyy} failed — LouisE";

            var lines = new List<string>
            {
                de
                    ? $"Die nächtliche Verlosung für den {summary.Date:dd.MM.yyyy} ist bei {summary.Failures.Count} von {summary.Succeeded + summary.Failures.Count} Standort-Zeitfenstern fehlgeschlagen (Versuch {attempt}). Die übrigen Zeitfenster wurden normal verlost."
                    : $"The nightly lottery for {summary.Date:dd.MM.yyyy} failed for {summary.Failures.Count} of {summary.Succeeded + summary.Failures.Count} location × time-slot pairs (attempt {attempt}). The remaining pairs were drawn normally."
            };
            lines.AddRange(summary.Failures.Select(f =>
                $"{f.LocationName} — {Localization.TimeSlotLabel(f.TimeSlot, admin.Language)}: {f.Error}"));
            lines.Add(de
                ? "Hangfire wiederholt den Lauf automatisch (bis zu 3 Versuche). Bleibt ein Zeitfenster offen, kann die Verlosung im Admin-Bereich manuell gestartet werden; ausstehende Buchungen werden sonst um 23:30 bzw. 05:00 Uhr vom Watchdog behandelt."
                : "Hangfire retries the run automatically (up to 3 attempts). If a pair stays open, the lottery can be started manually from the admin area; otherwise the watchdog handles the pending bookings at 23:30 / 05:00.");

            await _email.SendAdminAlertAsync(admin.Email, admin.DisplayName, subject, lines, admin.Language);
        }
    }

    public async Task LotteryWatchdogInterventionAsync(DateOnly targetDate, IReadOnlyList<string> healedSlots, int sweptPending)
    {
        foreach (var admin in await ActiveAdminsAsync())
        {
            var de = Localization.IsGerman(admin.Language);
            var subject = de
                ? $"Verlosungs-Watchdog hat eingegriffen ({targetDate:dd.MM.yyyy}) — LouisE"
                : $"Lottery watchdog intervened ({targetDate:dd.MM.yyyy}) — LouisE";

            var lines = new List<string>();
            if (healedSlots.Count > 0)
            {
                lines.Add(de
                    ? $"Für den {targetDate:dd.MM.yyyy} lagen ausstehende Buchungen ohne Verlosungslauf vor. Der Watchdog hat die Verlosung nachgeholt für:"
                    : $"Pending bookings for {targetDate:dd.MM.yyyy} had no lottery run. The watchdog ran the lottery for:");
                lines.AddRange(healedSlots);
            }
            if (sweptPending > 0)
            {
                lines.Add(de
                    ? $"{sweptPending} ausstehende Buchung(en) mit einem Datum in der Vergangenheit wurden auf „Verloren“ gesetzt — diese Tage wurden nie verlost."
                    : $"{sweptPending} pending booking(s) dated in the past were set to Lost — those days were never drawn.");
            }
            lines.Add(de
                ? "Bitte prüfen Sie die Anwendungsprotokolle rund um 22:00 Uhr auf die Ursache."
                : "Please check the application logs around 22:00 for the root cause.");

            await _email.SendAdminAlertAsync(admin.Email, admin.DisplayName, subject, lines, admin.Language);
        }
    }

    private string BaseUrl => _app.BaseUrl.TrimEnd('/');

    private sealed record AdminRecipient(string Email, string DisplayName, string Language);

    private async Task<List<AdminRecipient>> ActiveAdminsAsync() =>
        await _db.Users
            .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin) && u.DeletedAt == null)
            .Select(u => new AdminRecipient(u.Email, u.DisplayName, u.PreferredLanguage))
            .ToListAsync();
}
