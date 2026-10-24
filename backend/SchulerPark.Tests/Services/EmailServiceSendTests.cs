namespace SchulerPark.Tests.Services;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Tests.Integration;

// Full Send*Async templates through MailKit into an in-process loopback SMTP sink:
// language per recipient, HTML encoding of user data and fire-and-forget failure handling.
public class EmailServiceSendTests : IAsyncLifetime
{
    private readonly FakeSmtpServer _smtp = new();
    private readonly ListLogger<EmailService> _logger = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _smtp.DisposeAsync();

    private EmailService NewService(SmtpSettings? settings = null) => new(
        Options.Create(settings ?? new SmtpSettings { Host = "127.0.0.1", Port = _smtp.Port, FromAddress = "noreply@test.local" }),
        TestOptions.Booking,
        _logger);

    private static Booking BookingFor(string language, string displayName = "Anna", string slot = "17") => new()
    {
        Id = Guid.NewGuid(),
        User = new User { Email = "anna@schuler.de", DisplayName = displayName, PreferredLanguage = language },
        Location = new Location { Name = "Göppingen" },
        Date = new DateOnly(2026, 10, 5),
        TimeSlot = TimeSlot.Morning,
        ParkingSlot = new ParkingSlot { SlotNumber = slot },
        ConfirmationDeadline = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc)
    };

    private MimeMessage Single() => _smtp.Messages.Should().ContainSingle().Subject;

    [Theory]
    [InlineData("de", "E-Mail-Adresse bestätigen — LouisE", "E-Mail bestätigen")]
    [InlineData("en", "Verify your email address — LouisE", "Verify Email")]
    [InlineData("fr", "E-Mail-Adresse bestätigen — LouisE", "E-Mail bestätigen")]
    public async Task SendEmailVerificationAsync_SubjectAndBodyFollowLanguage(string language, string subject, string button)
    {
        await NewService().SendEmailVerificationAsync("new@schuler.de", "Ben", "https://app.test/verify?token=abc", language);

        var msg = Single();
        msg.Subject.Should().Be(subject);
        msg.To.Mailboxes.Single().Address.Should().Be("new@schuler.de");
        msg.HtmlBody.Should().Contain(button).And.Contain("https://app.test/verify?token=abc");
        msg.HtmlBody.Should().Contain($"<html lang=\"{(language == "en" ? "en" : "de")}\">");
    }

    [Fact]
    public async Task SendEmailVerificationAsync_EncodesDisplayName()
    {
        await NewService().SendEmailVerificationAsync("x@schuler.de", "<script>alert(1)</script>", "https://app.test/v", "en");

        var html = Single().HtmlBody;
        html.Should().NotContain("<script>").And.Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    public async Task SendBookingCreatedAsync_German_UsesUserLanguageAndLotteryTime()
    {
        await NewService().SendBookingCreatedAsync(BookingFor("de"));

        var msg = Single();
        msg.To.Mailboxes.Single().Address.Should().Be("anna@schuler.de");
        msg.Subject.Should().Be("Buchung eingegangen — Göppingen am 05.10.2026");
        msg.HtmlBody.Should().Contain("Hallo Anna,").And.Contain("Vormittag").And.Contain("21:00 Uhr");
        msg.HtmlBody.Should().NotContain("Booking Received");
    }

    [Fact]
    public async Task SendBookingCreatedAsync_English_UsesEnglishTemplate()
    {
        await NewService().SendBookingCreatedAsync(BookingFor("en"));

        var msg = Single();
        msg.Subject.Should().Be("Booking Received — Göppingen on 05.10.2026");
        msg.HtmlBody.Should().Contain("Hi Anna,").And.Contain("Morning").And.Contain("Please do not reply");
    }

    [Fact]
    public async Task SendLotteryWonAsync_ShowsBerlinDeadlineAndEncodesSlotNumber()
    {
        await NewService().SendLotteryWonAsync(BookingFor("en", slot: "<b>7</b>"));

        var html = Single().HtmlBody;
        // 05:00 UTC on 5 Oct 2026 is 07:00 CEST.
        html.Should().Contain("Please confirm your usage before 07:00 on 05.10.2026");
        html.Should().Contain("&lt;b&gt;7&lt;/b&gt;").And.NotContain("<b>7</b>");
    }

    [Fact]
    public async Task SendLotteryWonAsync_WithoutSlot_ShowsPlaceholder()
    {
        var booking = BookingFor("de");
        booking.ParkingSlot = null;

        await NewService().SendLotteryWonAsync(booking);

        Single().HtmlBody.Should().Contain("wird noch bekannt gegeben");
    }

    [Fact]
    public async Task SendBookingCancelledByAdminAsync_EncodesAndTrimsReason()
    {
        await NewService().SendBookingCancelledByAdminAsync(BookingFor("en"), "  <i>maintenance</i> & cleaning  ");

        var html = Single().HtmlBody;
        html.Should().Contain("<p><strong>Reason:</strong> &lt;i&gt;maintenance&lt;/i&gt; &amp; cleaning</p>");
    }

    [Fact]
    public async Task SendBookingCancelledByAdminAsync_BlankReason_OmitsReasonRow()
    {
        await NewService().SendBookingCancelledByAdminAsync(BookingFor("de"), "   ");

        var html = Single().HtmlBody;
        html.Should().NotContain("Grund:").And.Contain("von einem Administrator storniert");
    }

    [Fact]
    public async Task SendSlotReassignedAsync_EncodesOldSlotNumber()
    {
        await NewService().SendSlotReassignedAsync(BookingFor("de"), "<svg onload=x>");

        var html = Single().HtmlBody;
        html.Should().Contain("&lt;svg onload=x&gt;").And.NotContain("<svg");
        html.Should().Contain("<strong>Neuer Platz:</strong> 17");
    }

    [Fact]
    public async Task SendApprovalRequestToAdminAsync_UsesAdminLanguageAndEncodesPendingUser()
    {
        await NewService().SendApprovalRequestToAdminAsync(
            "admin@schuler.de", "Admin", "evil\"<x>@ext.com", "<img src=x>", "https://app.test/admin/users", "en");

        var msg = Single();
        msg.To.Mailboxes.Single().Address.Should().Be("admin@schuler.de");
        msg.Subject.Should().Be("New external user awaiting approval — LouisE");
        msg.HtmlBody.Should().Contain("&lt;img src=x&gt;").And.NotContain("<img");
        msg.HtmlBody.Should().Contain("evil&quot;&lt;x&gt;@ext.com");
    }

    [Theory]
    [InlineData("de", "Ihr Konto wurde freigegeben — LouisE")]
    [InlineData("en", "Your account has been approved — LouisE")]
    public async Task SendAccountApprovedAsync_SubjectFollowsLanguage(string language, string subject)
    {
        await NewService().SendAccountApprovedAsync("u@ext.com", "U", "https://app.test/login", language);

        Single().Subject.Should().Be(subject);
    }

    [Fact]
    public async Task SendAdminAlertAsync_EncodesSubjectHeadingAndParagraphs()
    {
        await NewService().SendAdminAlertAsync(
            "admin@schuler.de", "Admin", "Lottery <failed>", new[] { "slot <Morning> & more", "second" }, "en");

        var msg = Single();
        msg.Subject.Should().Be("Lottery <failed>");
        msg.HtmlBody.Should().Contain("Lottery &lt;failed&gt;</h2>");
        msg.HtmlBody.Should().Contain("<p>slot &lt;Morning&gt; &amp; more</p>").And.Contain("<p>second</p>");
    }

    [Theory]
    [InlineData("de", "Konto vorübergehend gesperrt — LouisE", "15 Minute(n)")]
    [InlineData("en", "Account temporarily locked — LouisE", "15 minute(s)")]
    public async Task SendAccountLockedAsync_ShowsLockoutMinutesInLanguage(string language, string subject, string minutes)
    {
        await NewService().SendAccountLockedAsync("u@schuler.de", "U", 15, "https://app.test/forgot", language);

        var msg = Single();
        msg.Subject.Should().Be(subject);
        msg.HtmlBody.Should().Contain(minutes).And.Contain("https://app.test/forgot");
    }

    [Fact]
    public async Task SendPasswordResetAsync_English_MentionsOneHourSingleUse()
    {
        await NewService().SendPasswordResetAsync("u@schuler.de", "U", "https://app.test/reset?token=t", "en");

        Single().HtmlBody.Should().Contain("valid for 1 hour and can be used once");
    }

    [Fact]
    public async Task SendAnyAsync_SmtpNotConfigured_SendsNothing()
    {
        await NewService(new SmtpSettings { Host = "" }).SendBookingCreatedAsync(BookingFor("de"));

        _smtp.Connections.Should().Be(0);
        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SendAnyAsync_SmtpUnreachable_LogsErrorAndDoesNotThrow()
    {
        // Grab a free port, then close it so the connection is refused.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var service = NewService(new SmtpSettings { Host = "127.0.0.1", Port = deadPort });
        var act = () => service.SendLotteryLostAsync(BookingFor("en"));

        await act.Should().NotThrowAsync();
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("anna@schuler.de"));
    }
}

/// <summary>Collects log entries so tests can assert what was logged.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Enqueue((logLevel, formatter(state, exception)));
}

/// <summary>
/// Minimal plain-text SMTP sink on loopback: advertises no extensions (no STARTTLS,
/// no AUTH), accepts every message and parses it with MimeKit. The 250 after DATA is
/// sent only once the message is stored, so it is visible as soon as the send returns.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _connections;

    public FakeSmtpServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port { get; }
    public int Connections => Volatile.Read(ref _connections);
    public ConcurrentQueue<MimeMessage> Messages { get; } = new();

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            Interlocked.Increment(ref _connections);
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.Latin1);
            await using var writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake.smtp ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                    case "HELO":
                        await writer.WriteLineAsync("250 fake.smtp");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go ahead");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            data.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                        using (var ms = new MemoryStream(Encoding.Latin1.GetBytes(data.ToString())))
                            Messages.Enqueue(await MimeMessage.LoadAsync(ms));
                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch { /* listener stopped */ }
        _cts.Dispose();
    }
}
