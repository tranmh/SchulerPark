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

    // ── Phase 20 WP1: capacity changes ──
    /// <summary>The booking kept its status but was moved from <paramref name="oldSlotNumber"/> to <c>booking.ParkingSlot</c>.</summary>
    Task SendSlotReassignedAsync(Booking booking, string oldSlotNumber);
    /// <summary>The booking lost its slot (blocked/deactivated) and is now on the waitlist.</summary>
    Task SendSlotWithdrawnAsync(Booking booking);
    /// <summary>An admin cancelled the booking (directly or by blocking/deactivating the location).</summary>
    Task SendBookingCancelledByAdminAsync(Booking booking, string? reason);
    /// <summary>Operational alert to one admin (lottery failure, watchdog intervention). Paragraphs are plain text, already localized.</summary>
    Task SendAdminAlertAsync(string adminEmail, string adminDisplayName, string subject, IReadOnlyList<string> paragraphs, string language);

    // ── Phase 20 WP4: confirmation model ──
    /// <summary>A slot freed up too close to (or after) the deadline: the booking was handed over already Confirmed.</summary>
    Task SendWaitlistAutoConfirmedAsync(Booking booking);
    /// <summary>The Won booking was not confirmed in time and has expired; the slot went back to the waitlist.</summary>
    Task SendBookingExpiredAsync(Booking booking);
    /// <summary>The Won booking was not confirmed in time, but nobody was waiting, so it was kept and is now Confirmed.</summary>
    Task SendUnconfirmedBookingKeptAsync(Booking booking);

    // ── Phase 20 WP2: password self-service ──
    Task SendPasswordResetAsync(string email, string displayName, string resetLink, string language);
    /// <summary>Reset requested for an SSO-only account: tells the user to sign in with Microsoft instead.</summary>
    Task SendPasswordResetNotApplicableAsync(string email, string displayName, string loginLink, string language);
    /// <summary>Account locked after repeated failures; the only in-band hint the owner gets.</summary>
    Task SendAccountLockedAsync(string email, string displayName, int lockoutMinutes, string forgotPasswordLink, string language);
}
