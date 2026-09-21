namespace SchulerPark.Infrastructure.Services;

using SchulerPark.Core.Entities;

public enum PushSendOutcome
{
    /// <summary>The push service accepted the message (2xx).</summary>
    Delivered,
    /// <summary>The push service reported the subscription as expired/unsubscribed (404/410).</summary>
    Gone,
    /// <summary>Any other error; already logged by the sender.</summary>
    Failed
}

/// <summary>
/// Thin seam over the Web Push transport so <see cref="PushNotificationService"/>
/// can be exercised in tests without a real push service or VAPID key pair.
/// </summary>
public interface IPushSender
{
    Task<PushSendOutcome> SendAsync(PushSubscription subscription, string jsonPayload, CancellationToken ct = default);
}
