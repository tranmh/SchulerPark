namespace SchulerPark.Api.DTOs.Booking;

/// <summary>Server-side bookable window in Europe/Berlin (WP3 3.1 / 2.1).</summary>
public record BookingWindowDto(
    DateOnly Today,
    DateOnly MinDate,
    DateOnly MaxDate,
    int MaxDaysAhead,
    bool MorningOpenToday,
    bool AfternoonOpenToday);
