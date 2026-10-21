using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Infrastructure.Services;

namespace SchulerPark.Tests.Integration;

/// <summary>POST /api/feedback: slot problems and general feedback are mailed to every admin.</summary>
[Collection("Integration")]
public class FeedbackTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FeedbackTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private DateOnly Today => DeadlineHelper.BerlinToday(_factory.Clock.GetUtcNow().UtcDateTime);

    private async Task<(AuthTestHelper.AuthResult User, Guid BookingId)> UserWithBookingAsync(
        DateOnly date, BookingStatus status = BookingStatus.Won)
    {
        var user = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.User, "reporter");
        var (locationId, slots) = await AdminTestHelper.SeedLocationAsync(_factory, 2);
        var slotId = status is BookingStatus.Won or BookingStatus.Confirmed ? slots[1] : (Guid?)null;
        var bookingId = await AdminTestHelper.SeedBookingAsync(
            _factory, user.User.Id, locationId, slotId, date, TimeSlot.Morning, status);
        return (user, bookingId);
    }

    private Task<HttpResponseMessage> PostAsync(string token, object body) =>
        _client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post, "/api/feedback", token, body));

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task SlotProblem_OnTodaysBooking_MailsEveryAdminInTheirLanguage()
    {
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "fb-admin");
        var super = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.SuperAdmin, "fb-super");
        var bystander = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.User, "fb-user");
        await AdminTestHelper.WithDbAsync(_factory, async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Id == admin.User.Id);
            u.PreferredLanguage = "de";
            var s = await db.Users.SingleAsync(x => x.Id == super.User.Id);
            s.PreferredLanguage = "en";
            await db.SaveChangesAsync();
        });
        var (reporter, bookingId) = await UserWithBookingAsync(Today);

        var response = await PostAsync(reporter.AccessToken, new
        {
            category = "SlotOccupied",
            bookingId,
            plateCountry = "DE",
            plateType = "Electric",
            plateText = "GP-AB 1234 E",
            comment = "Steht seit 8 Uhr da"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var alerts = _factory.Emails.AdminAlerts.ToList();

        var de = alerts.Single(a => a.Email == admin.User.Email);
        de.Subject.Should().StartWith("Parkplatz-Problem: P002 (");
        de.Paragraphs.Should().Contain("Problem: Parkplatz ist belegt");
        de.Paragraphs.Should().Contain(p => p.Contains("GP-AB 1234 E") && p.Contains("elektro"));
        de.Paragraphs.Should().Contain(p => p.Contains("Steht seit 8 Uhr da"));
        de.Paragraphs.Should().Contain(p => p.StartsWith("Melder:") && p.Contains(reporter.User.Email));

        var en = alerts.Single(a => a.Email == super.User.Email);
        en.Subject.Should().StartWith("Parking spot problem: P002");
        en.Paragraphs.Should().Contain("Problem: Spot is occupied");

        alerts.Should().NotContain(a => a.Email == bystander.User.Email || a.Email == reporter.User.Email);
    }

    [Fact]
    public async Task SlotProblem_PlateIgnoredForOtherCategories()
    {
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "fb-admin2");
        var (reporter, bookingId) = await UserWithBookingAsync(Today, BookingStatus.Confirmed);

        var response = await PostAsync(reporter.AccessToken,
            new { category = "ChargerFault", bookingId, plateText = "XX-YY 1" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var alert = _factory.Emails.AdminAlerts.Single(a => a.Email == admin.User.Email && a.Paragraphs.Any(p => p.Contains(reporter.User.Email)));
        alert.Paragraphs.Should().NotContain(p => p.Contains("XX-YY 1"));
    }

    [Fact]
    public async Task SlotProblem_SomeoneElsesBooking_Forbidden()
    {
        var (_, bookingId) = await UserWithBookingAsync(Today);
        var other = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.User, "fb-other");

        var response = await PostAsync(other.AccessToken, new { category = "SlotUnusable", bookingId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SlotProblem_TomorrowsBooking_Rejected()
    {
        var (reporter, bookingId) = await UserWithBookingAsync(Today.AddDays(1));

        var response = await PostAsync(reporter.AccessToken, new { category = "SlotNotFound", bookingId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("feedback_booking_not_today");
    }

    [Fact]
    public async Task SlotProblem_PendingBooking_Rejected()
    {
        var (reporter, bookingId) = await UserWithBookingAsync(Today, BookingStatus.Pending);

        var response = await PostAsync(reporter.AccessToken, new { category = "SlotNotFound", bookingId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("feedback_booking_not_assigned");
    }

    [Fact]
    public async Task General_NeedsComment_ThenMailsAdmins()
    {
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.Admin, "fb-admin3");
        var user = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.User, "fb-general");

        var empty = await PostAsync(user.AccessToken, new { category = "General", comment = "   " });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(empty)).Should().Be("feedback_comment_required");

        var ok = await PostAsync(user.AccessToken, new { category = "general", comment = "Tolle App" });
        ok.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Emails.AdminAlerts.Should().Contain(a =>
            a.Email == admin.User.Email && a.Paragraphs.Any(p => p.Contains("Tolle App")) &&
            a.Paragraphs.Any(p => p.Contains(user.User.Email)));
    }

    [Fact]
    public async Task UnknownCategory_Rejected()
    {
        var user = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.User, "fb-cat");

        foreach (var category in new[] { "Nonsense", "3" })
        {
            var response = await PostAsync(user.AccessToken, new { category, comment = "x" });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeAsync(response)).Should().Be("feedback_invalid_category");
        }
    }

    [Fact]
    public async Task TooManySubmissions_RateLimitedPerUser()
    {
        var user = await AdminTestHelper.CreateUserWithRoleAsync(_factory, _client, UserRole.User, "fb-spam");

        for (var i = 0; i < FeedbackService.MaxSubmissionsPerWindow; i++)
            (await PostAsync(user.AccessToken, new { category = "General", comment = $"#{i}" }))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var blocked = await PostAsync(user.AccessToken, new { category = "General", comment = "one more" });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(blocked)).Should().Be("feedback_rate_limited");
    }

    [Fact]
    public async Task Anonymous_Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/feedback", new { category = "General", comment = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
