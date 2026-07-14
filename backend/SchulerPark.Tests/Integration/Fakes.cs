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
/// login flow; every other email type is a no-op.
/// </summary>
public class CapturingEmailService : IEmailService
{
    private readonly ConcurrentDictionary<string, string> _verificationLinks = new();

    public string? VerificationLinkFor(string email) =>
        _verificationLinks.TryGetValue(email.ToLowerInvariant(), out var link) ? link : null;

    public string? VerificationTokenFor(string email)
    {
        var link = VerificationLinkFor(email);
        if (link == null) return null;
        var marker = "token=";
        var idx = link.IndexOf(marker, StringComparison.Ordinal);
        return idx < 0 ? null : link[(idx + marker.Length)..];
    }

    public Task SendEmailVerificationAsync(string email, string displayName, string verificationLink)
    {
        _verificationLinks[email.ToLowerInvariant()] = verificationLink;
        return Task.CompletedTask;
    }

    public Task SendBookingCreatedAsync(Booking booking) => Task.CompletedTask;
    public Task SendBookingCancelledAsync(Booking booking) => Task.CompletedTask;
    public Task SendLotteryWonAsync(Booking booking) => Task.CompletedTask;
    public Task SendLotteryLostAsync(Booking booking) => Task.CompletedTask;
    public Task SendConfirmationReminderAsync(Booking booking) => Task.CompletedTask;
    public Task SendWaitlistWonAsync(Booking booking) => Task.CompletedTask;
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
