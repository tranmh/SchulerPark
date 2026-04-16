namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Enums;

public interface IWaitlistService
{
    Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId);
}
