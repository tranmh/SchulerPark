using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Records verification emails so tests can complete the register → verify →
/// login flow; every other email type is recorded by type + booking id so
/// tests can assert which notifications were (not) sent.
/// </summary>
public class CapturingEmailService : IEmailService
{
    private readonly ConcurrentDictionary<string, string> _verificationLinks = new();
    private readonly ConcurrentDictionary<string, string> _resetLinks = new();

    public ConcurrentQueue<(string Type, Guid BookingId)> Sent { get; } = new();

    public string? VerificationLinkFor(string email) =>
        _verificationLinks.TryGetValue(email.ToLowerInvariant(), out var link) ? link : null;

    public string? VerificationTokenFor(string email) => TokenOf(VerificationLinkFor(email));

    /// <summary>Phase 20 WP2: the last password-reset link sent to the address (null if none).</summary>
    public string? ResetLinkFor(string email) =>
        _resetLinks.TryGetValue(email.ToLowerInvariant(), out var link) ? link : null;

    public string? ResetTokenFor(string email) => TokenOf(ResetLinkFor(email));

    private static string? TokenOf(string? link)
    {
        if (link == null) return null;
        var marker = "token=";
        var idx = link.IndexOf(marker, StringComparison.Ordinal);
        return idx < 0 ? null : link[(idx + marker.Length)..];
    }

    public Task SendEmailVerificationAsync(string email, string displayName, string verificationLink, string language)
    {
        _verificationLinks[email.ToLowerInvariant()] = verificationLink;
        return Task.CompletedTask;
    }

    private Task Record(string type, Booking booking)
    {
        Sent.Enqueue((type, booking.Id));
        return Task.CompletedTask;
    }

    // Phase 18 approval mails, recorded by type + recipient so tests can assert them.
    public ConcurrentQueue<(string Type, string Email)> ApprovalMails { get; } = new();

    /// <summary>Account-level mails (Phase 20: reset, reset-not-applicable, locked, admin alerts) by type + recipient.</summary>
    public ConcurrentQueue<(string Type, string Email)> AccountMails { get; } = new();

    /// <summary>Admin alert subjects, so tests can assert what an admin was told.</summary>
    public ConcurrentQueue<(string Email, string Subject, IReadOnlyList<string> Paragraphs)> AdminAlerts { get; } = new();

    public Task SendApprovalRequestToAdminAsync(string adminEmail, string adminDisplayName, string pendingUserEmail, string pendingUserDisplayName, string approvalLink, string adminLanguage)
    {
        ApprovalMails.Enqueue(("ApprovalRequest", adminEmail));
        return Task.CompletedTask;
    }

    public Task SendAccountApprovedAsync(string email, string displayName, string loginLink, string language)
    {
        ApprovalMails.Enqueue(("AccountApproved", email));
        return Task.CompletedTask;
    }

    public Task SendAccountRejectedAsync(string email, string displayName, string language)
    {
        ApprovalMails.Enqueue(("AccountRejected", email));
        return Task.CompletedTask;
    }

    public Task SendBookingCreatedAsync(Booking booking) => Record("BookingCreated", booking);
    public Task SendBookingCancelledAsync(Booking booking) => Record("BookingCancelled", booking);
    public Task SendLotteryWonAsync(Booking booking) => Record("LotteryWon", booking);
    public Task SendLotteryLostAsync(Booking booking) => Record("LotteryLost", booking);
    public Task SendConfirmationReminderAsync(Booking booking) => Record("ConfirmationReminder", booking);
    public Task SendWaitlistWonAsync(Booking booking) => Record("WaitlistWon", booking);
    public Task SendBookingDirectlyConfirmedAsync(Booking booking) => Record("DirectlyConfirmed", booking);
    public Task SendBookingWaitlistedAsync(Booking booking) => Record("Waitlisted", booking);

    // Phase 20 WP1
    public Task SendSlotReassignedAsync(Booking booking, string oldSlotNumber) => Record("SlotReassigned", booking);
    public Task SendSlotWithdrawnAsync(Booking booking) => Record("SlotWithdrawn", booking);
    public Task SendBookingCancelledByAdminAsync(Booking booking, string? reason) => Record("CancelledByAdmin", booking);

    // Phase 20 WP4
    public Task SendWaitlistAutoConfirmedAsync(Booking booking) => Record("WaitlistAutoConfirmed", booking);
    public Task SendBookingExpiredAsync(Booking booking) => Record("BookingExpired", booking);
    public Task SendUnconfirmedBookingKeptAsync(Booking booking) => Record("UnconfirmedKept", booking);

    public Task SendAdminAlertAsync(string adminEmail, string adminDisplayName, string subject, IReadOnlyList<string> paragraphs, string language)
    {
        AccountMails.Enqueue(("AdminAlert", adminEmail));
        AdminAlerts.Enqueue((adminEmail, subject, paragraphs));
        return Task.CompletedTask;
    }

    // Phase 20 WP2
    public Task SendPasswordResetAsync(string email, string displayName, string resetLink, string language)
    {
        _resetLinks[email.ToLowerInvariant()] = resetLink;
        AccountMails.Enqueue(("PasswordReset", email));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetNotApplicableAsync(string email, string displayName, string loginLink, string language)
    {
        AccountMails.Enqueue(("PasswordResetNotApplicable", email));
        return Task.CompletedTask;
    }

    public Task SendAccountLockedAsync(string email, string displayName, int lockoutMinutes, string forgotPasswordLink, string language)
    {
        AccountMails.Enqueue(("AccountLocked", email));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Push service stand-in for tests that construct services by hand: records every
/// notification by type + booking id, sends nothing.
/// </summary>
public class RecordingPushService : IPushNotificationService
{
    public ConcurrentQueue<(string Type, Guid BookingId)> Sent { get; } = new();

    private Task Record(string type, Booking booking)
    {
        Sent.Enqueue((type, booking.Id));
        return Task.CompletedTask;
    }

    public Task SendLotteryWonAsync(Booking booking) => Record("LotteryWon", booking);
    public Task SendLotteryLostAsync(Booking booking) => Record("LotteryLost", booking);
    public Task SendWaitlistWonAsync(Booking booking) => Record("WaitlistWon", booking);
    public Task SendBookingDirectlyConfirmedAsync(Booking booking) => Record("DirectlyConfirmed", booking);
    public Task SendBookingWaitlistedAsync(Booking booking) => Record("Waitlisted", booking);
    public Task SendSlotReassignedAsync(Booking booking, string oldSlotNumber) => Record("SlotReassigned", booking);
    public Task SendSlotWithdrawnAsync(Booking booking) => Record("SlotWithdrawn", booking);
    public Task SendBookingCancelledByAdminAsync(Booking booking, string? reason) => Record("CancelledByAdmin", booking);
    public Task SendConfirmationReminderAsync(Booking booking) => Record("ConfirmationReminder", booking);
    public Task SendBookingExpiredAsync(Booking booking) => Record("BookingExpired", booking);
    public Task SendWaitlistAutoConfirmedAsync(Booking booking) => Record("WaitlistAutoConfirmed", booking);
    public Task SendUnconfirmedBookingKeptAsync(Booking booking) => Record("UnconfirmedKept", booking);
    public Task<Core.Models.PushSendResult> SendTestAsync(Guid userId) =>
        Task.FromResult(Core.Models.PushSendResult.NoSubscriptions);
}

/// <summary>Waitlist stand-in for tests that construct services by hand: never promotes, no positions.</summary>
public sealed class NoopWaitlistService : IWaitlistService
{
    public Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, Core.Enums.TimeSlot timeSlot, Guid freedSlotId)
        => Task.CompletedTask;

    public Task<IReadOnlyDictionary<Guid, int>> GetWaitlistPositionsAsync(IReadOnlyCollection<Booking> bookings)
        => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());
}

/// <summary>Default <see cref="BookingSettings"/> (21:00 lottery, 07:00/13:00 deadlines, 2 h window) for hand-built services.</summary>
public static class TestOptions
{
    public static IOptions<BookingSettings> Booking => Options.Create(new BookingSettings());
}

/// <summary>
/// Stands in for the Web Push transport. Every send is recorded; endpoints listed
/// in <see cref="GoneEndpoints"/> are reported as expired (410) so tests can assert
/// that stale subscriptions get pruned, and <see cref="FailingEndpoints"/> simulate
/// a push service outage.
/// </summary>
public class RecordingPushSender : IPushSender
{
    public ConcurrentQueue<(string Endpoint, string Payload)> Sent { get; } = new();
    public ConcurrentDictionary<string, byte> GoneEndpoints { get; } = new();
    public ConcurrentDictionary<string, byte> FailingEndpoints { get; } = new();

    public Task<PushSendOutcome> SendAsync(PushSubscription subscription, string jsonPayload, CancellationToken ct = default)
    {
        if (GoneEndpoints.ContainsKey(subscription.Endpoint))
            return Task.FromResult(PushSendOutcome.Gone);
        if (FailingEndpoints.ContainsKey(subscription.Endpoint))
            return Task.FromResult(PushSendOutcome.Failed);

        Sent.Enqueue((subscription.Endpoint, jsonPayload));
        return Task.FromResult(PushSendOutcome.Delivered);
    }
}

/// <summary>
/// Accepts "tokens" of the form <c>fake|oid|email|name</c> so tests can drive
/// the Azure AD account-linking logic without a real tenant.
/// </summary>
public class FakeAzureAdTokenValidator : AzureAdTokenValidator
{
    public FakeAzureAdTokenValidator(IOptions<AzureAdSettings> settings, ILogger<AzureAdTokenValidator> logger)
        : base(settings, logger) { }

    public override Task<AzureAdUserInfo?> ValidateTokenAsync(string idToken)
    {
        var parts = idToken.Split('|');
        if (parts.Length != 4 || parts[0] != "fake")
            return Task.FromResult<AzureAdUserInfo?>(null);

        return Task.FromResult<AzureAdUserInfo?>(new AzureAdUserInfo(parts[1], parts[2], parts[3]));
    }

    public static string Token(string oid, string email, string name = "Azure User") =>
        $"fake|{oid}|{email}|{name}";
}

/// <summary>
/// Phase 20: the app's <see cref="TimeProvider"/> in tests. Passes the system clock
/// through until a test pins <see cref="UtcNow"/>; tests reset it in a finally block
/// (the Integration collection runs sequentially, so no two tests overlap).
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset? _utcNow;

    /// <summary>Pinned UTC instant, or null to follow the system clock.</summary>
    public DateTimeOffset? UtcNow
    {
        get => _utcNow;
        set => _utcNow = value;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow ?? base.GetUtcNow();

    /// <summary>Pins the clock and returns a scope that unpins it on dispose.</summary>
    public IDisposable Pin(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
        return new Unpin(this);
    }

    private sealed class Unpin(MutableTimeProvider owner) : IDisposable
    {
        public void Dispose() => owner._utcNow = null;
    }
}
