namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface IPushNotificationService
{
    Task SendLotteryWonAsync(Booking booking);
    Task SendLotteryLostAsync(Booking booking);
    Task SendWaitlistWonAsync(Booking booking);
}
