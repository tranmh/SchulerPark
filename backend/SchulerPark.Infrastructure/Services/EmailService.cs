namespace SchulerPark.Infrastructure.Services;

using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;

public class EmailService : IEmailService
{
    private readonly SmtpSettings _smtp;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpSettings> smtp, ILogger<EmailService> logger)
    {
        _smtp = smtp.Value;
        _logger = logger;
    }

    public async Task SendBookingCreatedAsync(Booking booking)
    {
        var subject = $"Booking Confirmed — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml($"""
            <h2>Booking Confirmed</h2>
            <p>Hi {booking.User.DisplayName},</p>
            <p>Your parking booking has been placed:</p>
            {BookingDetailsTable(booking)}
            <p>Your booking is now <strong>Pending</strong>. The lottery will run at 10 PM and you will be notified of the result.</p>
            """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendBookingCancelledAsync(Booking booking)
    {
        var subject = $"Booking Cancelled — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml($"""
            <h2>Booking Cancelled</h2>
            <p>Hi {booking.User.DisplayName},</p>
            <p>Your parking booking has been cancelled:</p>
            {BookingDetailsTable(booking)}
            <p>You can book again anytime.</p>
            """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendLotteryWonAsync(Booking booking)
    {
        var deadline = DeadlineHelper.GetConfirmationDeadline(booking.Date, booking.TimeSlot);
        var berlinDeadline = TimeZoneInfo.ConvertTimeFromUtc(deadline,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

        var subject = $"You Won! — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml($"""
            <h2 style="color: #16a34a;">You Won a Parking Spot!</h2>
            <p>Hi {booking.User.DisplayName},</p>
            <p>Great news! You have been assigned a parking spot:</p>
            {BookingDetailsTable(booking)}
            <p><strong>Assigned Slot:</strong> {booking.ParkingSlot?.SlotNumber ?? "TBD"}</p>
            <p style="color: #d97706;"><strong>Please confirm your usage before {berlinDeadline:HH:mm} on {berlinDeadline:dd.MM.yyyy}.</strong></p>
            <p>Log in to SchulerPark and confirm your booking, or it will expire.</p>
            """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendLotteryLostAsync(Booking booking)
    {
        var subject = $"Lottery Result — {booking.Location.Name} on {booking.Date:dd.MM.yyyy}";
        var body = BuildHtml($"""
            <h2>Lottery Result</h2>
            <p>Hi {booking.User.DisplayName},</p>
            <p>Unfortunately, you were not selected in the parking lottery this time:</p>
            {BookingDetailsTable(booking)}
            <p>Demand exceeded available spots. Better luck next time!</p>
            """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    public async Task SendConfirmationReminderAsync(Booking booking)
    {
        var subject = $"Reminder: Confirm Your Parking — {booking.Location.Name}";
        var body = BuildHtml($"""
            <h2 style="color: #d97706;">Confirmation Reminder</h2>
            <p>Hi {booking.User.DisplayName},</p>
            <p>Your parking booking is about to expire because it has not been confirmed:</p>
            {BookingDetailsTable(booking)}
            <p><strong>Please log in to SchulerPark and confirm your booking now.</strong></p>
            """);

        await SendEmailAsync(booking.User.Email, subject, body);
    }

    private static string BookingDetailsTable(Booking booking) => $"""
        <table style="border-collapse: collapse; margin: 16px 0;">
            <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">Location</td><td style="padding: 4px 0;"><strong>{booking.Location.Name}</strong></td></tr>
            <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">Date</td><td style="padding: 4px 0;"><strong>{booking.Date:dd.MM.yyyy}</strong></td></tr>
            <tr><td style="padding: 4px 16px 4px 0; color: #6b7280;">Time Slot</td><td style="padding: 4px 0;"><strong>{booking.TimeSlot}</strong></td></tr>
        </table>
        """;

    private static string BuildHtml(string content) => $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #1f2937; max-width: 600px; margin: 0 auto; padding: 20px;">
            {content}
            <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
            <p style="font-size: 12px; color: #9ca3af;">This is an automated message from SchulerPark. Please do not reply.</p>
        </body>
        </html>
        """;

    private async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        if (!_smtp.IsConfigured)
        {
            _logger.LogDebug("SMTP not configured, skipping email to {To}: {Subject}", to, subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_smtp.Host, _smtp.Port, MailKit.Security.SecureSocketOptions.Auto);
            if (!string.IsNullOrEmpty(_smtp.Username))
                await client.AuthenticateAsync(_smtp.Username, _smtp.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", to, subject);
        }
    }
}
