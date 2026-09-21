namespace SchulerPark.Tests.Services;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Infrastructure.Services;
using Xunit;

// Phase 20: emails follow the user's PreferredLanguage; German is the default.
public class EmailServiceLocalizationTests
{
    private static Booking MorningBooking() => new()
    {
        Location = new Location { Name = "Göppingen", Address = "" },
        Date = new DateOnly(2026, 10, 5),
        TimeSlot = TimeSlot.Morning,
    };

    [Fact]
    public void BookingDetailsTable_is_German_for_de()
    {
        var html = EmailService.BookingDetailsTable(MorningBooking(), "de");

        Assert.Contains("Standort", html);
        Assert.Contains("Zeitfenster", html);
        Assert.Contains("Vormittag", html);
        Assert.DoesNotContain("Morning", html);
    }

    [Fact]
    public void BookingDetailsTable_is_English_for_en()
    {
        var html = EmailService.BookingDetailsTable(MorningBooking(), "en");

        Assert.Contains("Location", html);
        Assert.Contains("Time Slot", html);
        Assert.Contains("Morning", html);
        Assert.DoesNotContain("Vormittag", html);
    }

    [Fact]
    public void Greeting_uses_formal_German()
    {
        Assert.StartsWith("<p>Hallo ", EmailService.Greeting("Anna", "de"));
        Assert.StartsWith("<p>Hi ", EmailService.Greeting("Anna", "en"));
    }

    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("de", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("EN", "en")]
    [InlineData("fr", "de")]
    public void Normalize_maps_any_tag_onto_a_supported_language(string? input, string expected)
    {
        Assert.Equal(expected, Localization.Normalize(input));
    }

    [Theory]
    [InlineData("de", true)]
    [InlineData("en", true)]
    [InlineData("EN", true)]
    [InlineData("en-GB", false)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void IsSupported_accepts_only_the_two_exact_codes(string? input, bool expected)
    {
        Assert.Equal(expected, Localization.IsSupported(input));
    }

    [Fact]
    public void TimeSlotLabel_translates_both_slots()
    {
        Assert.Equal("Nachmittag", Localization.TimeSlotLabel(TimeSlot.Afternoon, "de"));
        Assert.Equal("Afternoon", Localization.TimeSlotLabel(TimeSlot.Afternoon, "en"));
    }
}
