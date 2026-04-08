namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

public interface ILocationService
{
    Task<List<Location>> GetActiveLocationsAsync();
    Task<List<ParkingSlot>> GetLocationSlotsAsync(Guid locationId);
    Task<List<BlockedDay>> GetBlockedDaysAsync(Guid locationId, DateOnly from, DateOnly to);
    Task<List<(DateOnly Date, TimeSlot TimeSlot, int Available, int Total, int Booked)>>
        GetAvailabilityAsync(Guid locationId, DateOnly from, DateOnly to);
}
