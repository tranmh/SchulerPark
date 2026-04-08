namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface IEmailService
{
    Task SendBookingCreatedAsync(Booking booking);
    Task SendBookingCancelledAsync(Booking booking);
    Task SendLotteryWonAsync(Booking booking);
    Task SendLotteryLostAsync(Booking booking);
    Task SendConfirmationReminderAsync(Booking booking);
}
