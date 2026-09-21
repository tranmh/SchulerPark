namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Models;

public interface IPushNotificationService
{
    Task SendLotteryWonAsync(Booking booking);
    Task SendLotteryLostAsync(Booking booking);
    Task SendWaitlistWonAsync(Booking booking);
    Task SendBookingDirectlyConfirmedAsync(Booking booking);
    Task SendBookingWaitlistedAsync(Booking booking);

    /// <summary>
    /// Sends a "push notifications are working" message to every device the user
    /// has subscribed, so they can verify the end-to-end path (browser permission →
    /// stored subscription → VAPID-signed delivery → service worker) by hand.
    /// </summary>
    Task<PushSendResult> SendTestAsync(Guid userId);
}
