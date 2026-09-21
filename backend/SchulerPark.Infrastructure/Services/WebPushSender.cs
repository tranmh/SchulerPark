namespace SchulerPark.Infrastructure.Services;

using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Settings;

/// <summary>
/// Production <see cref="IPushSender"/>: VAPID-signed delivery via Lib.Net.Http.WebPush.
/// </summary>
public class WebPushSender : IPushSender
{
    private readonly VapidSettings _vapid;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(IOptions<VapidSettings> vapid, ILogger<WebPushSender> logger)
    {
        _vapid = vapid.Value;
        _logger = logger;
    }

    public async Task<PushSendOutcome> SendAsync(Core.Entities.PushSubscription sub, string jsonPayload, CancellationToken ct = default)
    {
        try
        {
            var client = new PushServiceClient
            {
                DefaultAuthentication = new VapidAuthentication(_vapid.PublicKey, _vapid.PrivateKey)
                {
                    Subject = _vapid.Subject
                }
            };

            var pushSubscription = new PushSubscription
            {
                Endpoint = sub.Endpoint,
                Keys = new Dictionary<string, string>
                {
                    ["p256dh"] = sub.P256dh,
                    ["auth"] = sub.Auth
                }
            };

            var message = new PushMessage(jsonPayload) { Urgency = PushMessageUrgency.Normal };

            await client.RequestPushMessageDeliveryAsync(pushSubscription, message, ct);
            return PushSendOutcome.Delivered;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Push subscription {SubId} expired ({Status}), removing.", sub.Id, (int)ex.StatusCode);
            return PushSendOutcome.Gone;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push to subscription {SubId}.", sub.Id);
            return PushSendOutcome.Failed;
        }
    }
}
