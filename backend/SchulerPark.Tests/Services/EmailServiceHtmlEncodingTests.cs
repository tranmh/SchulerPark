namespace SchulerPark.Tests.Services;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services;
using Xunit;

// Bug #14: user-/admin-controlled fields must be HTML-encoded in email bodies.
public class EmailServiceHtmlEncodingTests
{
    [Fact]
    public void Greeting_encodes_display_name()
    {
        var html = EmailService.Greeting("<script>alert('x')</script>");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void BookingDetailsTable_encodes_location_name()
    {
        var booking = new Booking
        {
            Location = new Location { Name = "<img src=x onerror=alert(1)>", Address = "" },
            Date = new DateOnly(2026, 1, 1),
            TimeSlot = TimeSlot.Morning,
        };

        var html = EmailService.BookingDetailsTable(booking);

        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }
}
