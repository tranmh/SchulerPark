namespace SchulerPark.Api.DTOs.Location;

/// <summary>
/// WP3 3.2: <c>BookingCount</c> is Won + Confirmed (slots actually held). Demand before
/// the lottery is <c>PendingCount</c>; <c>WaitlistCount</c> is Lost bookings still waiting.
/// </summary>
public record AvailabilityDto(
    DateOnly Date,
    string TimeSlot,
    int AvailableSlots,
    int TotalSlots,
    int BookingCount,
    int PendingCount,
    int WaitlistCount,
    bool LotteryRan);
