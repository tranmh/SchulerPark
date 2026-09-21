namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

/// <summary>
/// All mails are sent in the recipient's language: booking mails read it from
/// <c>booking.User.PreferredLanguage</c>, the others take it as a parameter
/// (see <see cref="Helpers.Localization"/>).
/// </summary>
public interface IEmailService
{
    Task SendEmailVerificationAsync(string email, string displayName, string verificationLink, string language);
    Task SendBookingCreatedAsync(Booking booking);
    Task SendBookingCancelledAsync(Booking booking);
    Task SendLotteryWonAsync(Booking booking);
    Task SendLotteryLostAsync(Booking booking);
    Task SendConfirmationReminderAsync(Booking booking);
    Task SendWaitlistWonAsync(Booking booking);
    Task SendBookingDirectlyConfirmedAsync(Booking booking);
    Task SendBookingWaitlistedAsync(Booking booking);
    Task SendApprovalRequestToAdminAsync(string adminEmail, string adminDisplayName, string pendingUserEmail, string pendingUserDisplayName, string approvalLink, string adminLanguage);
    Task SendAccountApprovedAsync(string email, string displayName, string loginLink, string language);
    Task SendAccountRejectedAsync(string email, string displayName, string language);
}
