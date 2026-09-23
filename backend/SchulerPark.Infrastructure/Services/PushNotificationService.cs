namespace SchulerPark.Infrastructure.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

public class PushNotificationService : IPushNotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    private readonly IPushSender _sender;
    private readonly VapidSettings _vapid;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        AppDbContext db,
        IPushSender sender,
        IOptions<VapidSettings> vapid,
        ILogger<PushNotificationService> logger)
    {
        _db = db;
        _sender = sender;
        _vapid = vapid.Value;
        _logger = logger;
    }

    public Task SendLotteryWonAsync(Booking booking)
    {
        var (lang, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Parkplatz gewonnen!" : "You Won a Parking Spot!",
            Body = de
                ? $"{booking.Location.Name} am {booking.Date:dd.MM.yyyy} — {Localization.TimeSlotLabel(booking.TimeSlot, lang)}. Bitte bestätigen."
                : $"{booking.Location.Name} on {booking.Date:dd.MM.yyyy} — {Localization.TimeSlotLabel(booking.TimeSlot, lang)}. Please confirm.",
            Url = "/my-bookings"
        });
    }

    public Task SendLotteryLostAsync(Booking booking)
    {
        var (_, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Verlosungsergebnis" : "Lottery Result",
            Body = de
                ? $"Leider wurden Sie für {booking.Location.Name} am {booking.Date:dd.MM.yyyy} nicht ausgewählt."
                : $"Unfortunately you were not selected for {booking.Location.Name} on {booking.Date:dd.MM.yyyy}.",
            Url = "/my-bookings"
        });
    }

    public Task SendWaitlistWonAsync(Booking booking)
    {
        var (_, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Ein Platz ist frei geworden!" : "A Spot Opened Up!",
            Body = de
                ? $"Sie haben einen Platz in {booking.Location.Name} am {booking.Date:dd.MM.yyyy} erhalten. Bitte bestätigen."
                : $"You got a spot at {booking.Location.Name} on {booking.Date:dd.MM.yyyy}. Please confirm.",
            Url = "/my-bookings"
        });
    }

    public Task SendBookingDirectlyConfirmedAsync(Booking booking)
    {
        var (_, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Parkplatz bestätigt!" : "Parking Spot Confirmed!",
            Body = de
                ? $"Platz {booking.ParkingSlot?.SlotNumber} in {booking.Location.Name} am {booking.Date:dd.MM.yyyy} gehört Ihnen — nichts weiter zu tun."
                : $"Slot {booking.ParkingSlot?.SlotNumber} at {booking.Location.Name} on {booking.Date:dd.MM.yyyy} is yours — no action needed.",
            Url = "/my-bookings"
        });
    }

    public Task SendBookingWaitlistedAsync(Booking booking)
    {
        var (_, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Sie stehen auf der Warteliste" : "You're on the Waitlist",
            Body = de
                ? $"{booking.Location.Name} am {booking.Date:dd.MM.yyyy} ist voll. Wird ein Platz frei, erhalten Sie ihn automatisch."
                : $"{booking.Location.Name} on {booking.Date:dd.MM.yyyy} is full. You'll get a spot automatically if one frees up.",
            Url = "/my-bookings"
        });
    }

    public Task SendSlotReassignedAsync(Booking booking, string oldSlotNumber)
    {
        var (_, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Ihr Parkplatz wurde geändert" : "Your parking slot has changed",
            Body = de
                ? $"{booking.Location.Name} am {booking.Date:dd.MM.yyyy}: Platz {oldSlotNumber} ist nicht mehr verfügbar — Sie parken jetzt auf Platz {booking.ParkingSlot?.SlotNumber}."
                : $"{booking.Location.Name} on {booking.Date:dd.MM.yyyy}: slot {oldSlotNumber} is no longer available — you now park on slot {booking.ParkingSlot?.SlotNumber}.",
            Url = "/my-bookings"
        });
    }

    public Task SendSlotWithdrawnAsync(Booking booking)
    {
        var (_, de) = LanguageOf(booking);
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Parkplatz nicht mehr verfügbar" : "Parking slot no longer available",
            Body = de
                ? $"Ihr Platz in {booking.Location.Name} am {booking.Date:dd.MM.yyyy} wurde gesperrt. Sie stehen jetzt auf der Warteliste."
                : $"Your slot at {booking.Location.Name} on {booking.Date:dd.MM.yyyy} was blocked. You are now on the waitlist.",
            Url = "/my-bookings"
        });
    }

    public Task SendBookingCancelledByAdminAsync(Booking booking, string? reason)
    {
        var (_, de) = LanguageOf(booking);
        var why = string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason.Trim()})";
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = de ? "Buchung storniert" : "Booking cancelled",
            Body = de
                ? $"Ihre Buchung in {booking.Location.Name} am {booking.Date:dd.MM.yyyy} wurde von der Verwaltung storniert{why}."
                : $"Your booking at {booking.Location.Name} on {booking.Date:dd.MM.yyyy} was cancelled by an administrator{why}.",
            Url = "/my-bookings"
        });
    }

    public async Task<PushSendResult> SendTestAsync(Guid userId)
    {
        var language = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync();
        var de = Localization.IsGerman(language);

        return await SendToUserAsync(userId, new PushPayload
        {
            Title = de ? "LouisE Testbenachrichtigung" : "LouisE test notification",
            Body = de
                ? "Push-Benachrichtigungen funktionieren auf diesem Gerät."
                : "Push notifications are working on this device.",
            Url = "/profile"
        });
    }

    private static (string Language, bool IsGerman) LanguageOf(Booking booking)
    {
        var lang = Localization.Normalize(booking.User?.PreferredLanguage);
        return (lang, Localization.IsGerman(lang));
    }

    private async Task<PushSendResult> SendToUserAsync(Guid userId, PushPayload payload)
    {
        if (!_vapid.IsConfigured)
        {
            _logger.LogDebug("VAPID not configured, skipping push notification.");
            return PushSendResult.NotConfigured;
        }

        var subscriptions = await _db.PushSubscriptions
            .Where(ps => ps.UserId == userId)
            .ToListAsync();

        if (subscriptions.Count == 0) return PushSendResult.NoSubscriptions;

        var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);

        var delivered = 0;
        var failed = 0;
        var expiredSubscriptionIds = new List<Guid>();

        foreach (var sub in subscriptions)
        {
            switch (await _sender.SendAsync(sub, jsonPayload))
            {
                case PushSendOutcome.Delivered:
                    delivered++;
                    break;
                case PushSendOutcome.Gone:
                    expiredSubscriptionIds.Add(sub.Id);
                    break;
                default:
                    failed++;
                    break;
            }
        }

        if (expiredSubscriptionIds.Count > 0)
        {
            // Best effort: a failed cleanup must not turn a delivered fan-out into an
            // error for the caller — the stale rows are simply retried next time.
            try
            {
                await _db.PushSubscriptions
                    .Where(ps => expiredSubscriptionIds.Contains(ps.Id))
                    .ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prune {Count} expired push subscription(s).", expiredSubscriptionIds.Count);
            }
        }

        return new PushSendResult(subscriptions.Count, delivered, expiredSubscriptionIds.Count, failed);
    }

    private record PushPayload
    {
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public string Url { get; init; } = "/";
    }
}
