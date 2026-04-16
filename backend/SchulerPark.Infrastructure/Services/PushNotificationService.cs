namespace SchulerPark.Infrastructure.Services;

using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

public class PushNotificationService : IPushNotificationService
{
    private readonly AppDbContext _db;
    private readonly VapidSettings _vapid;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(AppDbContext db, IOptions<VapidSettings> vapid, ILogger<PushNotificationService> logger)
    {
        _db = db;
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

    private async Task SendToUserAsync(Guid userId, PushPayload payload)
    {
        if (!_vapid.IsConfigured)
        {
            _logger.LogDebug("VAPID not configured, skipping push notification.");
            return;
        }

        var subscriptions = await _db.PushSubscriptions
            .Where(ps => ps.UserId == userId)
            .ToListAsync();

        if (subscriptions.Count == 0) return;

        var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var authentication = new VapidAuthentication(_vapid.PublicKey, _vapid.PrivateKey)
        {
            Subject = _vapid.Subject
        };

        var client = new PushServiceClient();
        client.DefaultAuthentication = authentication;

        var expiredSubscriptionIds = new List<Guid>();

        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSubscription = new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = sub.P256dh,
                        ["auth"] = sub.Auth
                    }
                };

                var message = new PushMessage(jsonPayload)
                {
                    Urgency = PushMessageUrgency.Normal
                };

                await client.RequestPushMessageDeliveryAsync(pushSubscription, message);
            }
            catch (PushServiceClientException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogInformation("Push subscription {SubId} expired (410 Gone), removing.", sub.Id);
                expiredSubscriptionIds.Add(sub.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push to subscription {SubId}.", sub.Id);
            }
        }

        if (expiredSubscriptionIds.Count > 0)
        {
            await _db.PushSubscriptions
                .Where(ps => expiredSubscriptionIds.Contains(ps.Id))
                .ExecuteDeleteAsync();
        }
    }

    private record PushPayload
    {
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public string Url { get; init; } = "/";
    }
}
