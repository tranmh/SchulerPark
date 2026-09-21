namespace SchulerPark.Infrastructure.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
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
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = "You Won a Parking Spot!",
            Body = $"{booking.Location.Name} on {booking.Date:dd.MM.yyyy} — {booking.TimeSlot}. Please confirm.",
            Url = "/my-bookings"
        });
    }

    public Task SendLotteryLostAsync(Booking booking)
    {
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = "Lottery Result",
            Body = $"Unfortunately you were not selected for {booking.Location.Name} on {booking.Date:dd.MM.yyyy}.",
            Url = "/my-bookings"
        });
    }

    public Task SendWaitlistWonAsync(Booking booking)
    {
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = "A Spot Opened Up!",
            Body = $"You got a spot at {booking.Location.Name} on {booking.Date:dd.MM.yyyy}. Please confirm.",
            Url = "/my-bookings"
        });
    }

    public Task SendBookingDirectlyConfirmedAsync(Booking booking)
    {
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = "Parking Spot Confirmed!",
            Body = $"Slot {booking.ParkingSlot?.SlotNumber} at {booking.Location.Name} on {booking.Date:dd.MM.yyyy} is yours — no action needed.",
            Url = "/my-bookings"
        });
    }

    public Task SendBookingWaitlistedAsync(Booking booking)
    {
        return SendToUserAsync(booking.UserId, new PushPayload
        {
            Title = "You're on the Waitlist",
            Body = $"{booking.Location.Name} on {booking.Date:dd.MM.yyyy} is full. You'll get a spot automatically if one frees up.",
            Url = "/my-bookings"
        });
    }

    public Task<PushSendResult> SendTestAsync(Guid userId)
    {
        return SendToUserAsync(userId, new PushPayload
        {
            Title = "LouisE test notification",
            Body = "Push notifications are working on this device.",
            Url = "/profile"
        });
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
