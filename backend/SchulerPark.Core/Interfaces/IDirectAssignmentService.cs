namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public enum DirectAssignmentOutcome
{
    /// <summary>Lottery has not run for the booking's (location, date, timeSlot); booking stays Pending.</summary>
    NotApplicable,

    /// <summary>A free slot was assigned; booking is Confirmed immediately.</summary>
    AssignedConfirmed,

    /// <summary>Lottery ran but no slot is free; booking is Lost (waitlist-eligible).</summary>
    WaitlistedLost
}

public interface IDirectAssignmentService
{
    /// <summary>
    /// If the lottery already ran for the booking's (location, date, timeSlot),
    /// mutates the (not yet saved) booking: assigns a free slot and sets
    /// Status=Confirmed/ConfirmedAt, or sets Status=Lost when full.
    /// Does not call SaveChanges and does not write LotteryHistory.
    /// </summary>
    Task<DirectAssignmentOutcome> ApplyAsync(Booking booking);
}
