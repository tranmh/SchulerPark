namespace SchulerPark.Core.Models;

using SchulerPark.Core.Enums;

/// <summary>
/// Availability of one date × time slot at a location (WP3 3.2). Before the lottery the
/// meaningful signal is demand (<see cref="PendingCount"/> vs <see cref="Total"/>);
/// after it, free slots (<see cref="Available"/>).
/// </summary>
/// <param name="Available">Unblocked active slots not held by a Won/Confirmed booking.</param>
/// <param name="Total">Unblocked active slots.</param>
/// <param name="Booked">Won + Confirmed bookings (the ones that hold a slot).</param>
/// <param name="PendingCount">Lottery requests not yet decided.</param>
/// <param name="WaitlistCount">Lost bookings still waiting for a freed slot.</param>
/// <param name="LotteryRan">A <c>LotteryRun</c> row exists for this date × slot.</param>
public record SlotAvailability(
    DateOnly Date,
    TimeSlot TimeSlot,
    int Available,
    int Total,
    int Booked,
    int PendingCount,
    int WaitlistCount,
    bool LotteryRan);
