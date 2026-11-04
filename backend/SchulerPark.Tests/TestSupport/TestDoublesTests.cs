namespace SchulerPark.Tests.TestSupport;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services;

public class TestDoublesTests
{
    [Fact]
    public async Task SeedSite_InMemoryDb_PersistsBuildersWithNavigations()
    {
        await using var db = TestDb.InMemory();
        var clock = new FixedTimeProvider();
        var (user, location, slots) = TestData.SeedSite(db, slotCount: 2);

        db.Bookings.Add(TestData.Booking(user, location, clock.Today.AddDays(1), slot: slots[0],
            status: BookingStatus.Won));
        db.BlockedDays.Add(TestData.BlockedDay(location, clock.Today.AddDays(2), TestData.Admin()));
        await db.SaveChangesAsync();

        var booking = await db.Bookings.Include(b => b.ParkingSlot).SingleAsync();
        booking.ParkingSlot!.SlotNumber.Should().Be("A-1");
        booking.Date.Should().Be(new DateOnly(2026, 3, 3));
        user.DisplayName.Should().Be("Test User");
        (await db.BlockedDays.CountAsync()).Should().Be(1);

        clock.Advance(TimeSpan.FromDays(1));
        clock.Today.Should().Be(new DateOnly(2026, 3, 3));
    }

    [Fact]
    public async Task FakeTokenService_RevokeAllUserTokens_InvalidatesOnlyThatUsersTokens()
    {
        var tokens = new FakeTokenService();
        var notifier = new RecordingAdminNotifier();
        var a = TestData.User();
        var b = TestData.User();
        var ra = await tokens.GenerateRefreshTokenAsync(a, "127.0.0.1");
        var rb = await tokens.GenerateRefreshTokenAsync(b, null);

        await tokens.RevokeAllUserTokensAsync(a.Id);
        await notifier.PendingUserAsync(TestData.PendingUser());

        (await tokens.ValidateRefreshTokenAsync(ra)).Should().BeNull();
        (await tokens.ValidateRefreshTokenAsync(rb))!.Id.Should().Be(b.Id);
        tokens.Revoked.Should().ContainSingle().Which.Should().Be(ra);
        tokens.GenerateAccessToken(a).Should().Be($"access:{a.Id}");
        notifier.PendingUsers.Should().ContainSingle();
        SeedSql.BookingsAndBlockedDay.Should().Contain(SeedSql.WonBookingId.ToString());
    }

    [Fact]
    public async Task SendEmailVerificationAsync_SmtpUnconfigured_LogsSkippedSubject()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = new EmailService(TestSettings.SmtpUnconfigured, TestSettings.Booking(), logger);

        await service.SendEmailVerificationAsync("a@schuler.de", "Ada", "https://x/verify?token=t", "en");

        logger.Messages(LogLevel.Debug).Should().ContainSingle()
            .Which.Should().Contain("a@schuler.de").And.Contain("Verify your email address");
    }
}
