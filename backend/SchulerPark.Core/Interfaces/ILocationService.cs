namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Models;

public interface ILocationService
{
    Task<List<Location>> GetActiveLocationsAsync();
    Task<List<ParkingSlot>> GetLocationSlotsAsync(Guid locationId);
    Task<List<BlockedDay>> GetBlockedDaysAsync(Guid locationId, DateOnly from, DateOnly to);
    Task<List<SlotAvailability>> GetAvailabilityAsync(Guid locationId, DateOnly from, DateOnly to);
}
