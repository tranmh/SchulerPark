namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface IEmailService
{
    Task SendEmailVerificationAsync(string email, string displayName, string verificationLink);
    Task SendBookingCreatedAsync(Booking booking);
    Task SendBookingCancelledAsync(Booking booking);
    Task SendLotteryWonAsync(Booking booking);
    Task SendLotteryLostAsync(Booking booking);
    Task SendConfirmationReminderAsync(Booking booking);
    Task SendWaitlistWonAsync(Booking booking);
}
