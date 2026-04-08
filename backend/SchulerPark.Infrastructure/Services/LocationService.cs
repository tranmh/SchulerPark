namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

public class LocationService : ILocationService
{
    private readonly AppDbContext _db;

    public LocationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Location>> GetActiveLocationsAsync()
    {
        return await _db.Locations
            .Where(l => l.IsActive)
            .Include(l => l.ParkingSlots.Where(s => s.IsActive))
            .OrderBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<List<ParkingSlot>> GetLocationSlotsAsync(Guid locationId)
    {
        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId && l.IsActive);
        if (!locationExists)
            throw new NotFoundException("Location not found or inactive.");

        return await _db.ParkingSlots
            .Where(s => s.LocationId == locationId && s.IsActive)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();
    }

    public async Task<List<BlockedDay>> GetBlockedDaysAsync(Guid locationId, DateOnly from, DateOnly to)
    {
        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId && l.IsActive);
        if (!locationExists)
            throw new NotFoundException("Location not found or inactive.");

        return await _db.BlockedDays
            .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to)
            .OrderBy(b => b.Date)
            .ToListAsync();
    }

    public async Task<List<(DateOnly Date, TimeSlot TimeSlot, int Available, int Total, int Booked)>>
        GetAvailabilityAsync(Guid locationId, DateOnly from, DateOnly to)
    {
        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId && l.IsActive);
        if (!locationExists)
            throw new NotFoundException("Location not found or inactive.");

        var activeSlotCount = await _db.ParkingSlots
            .CountAsync(s => s.LocationId == locationId && s.IsActive);

        var activeSlotIds = await _db.ParkingSlots
            .Where(s => s.LocationId == locationId && s.IsActive)
            .Select(s => s.Id)
            .ToListAsync();

        // Batch load blocked days in range
        var blockedDays = await _db.BlockedDays
            .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to)
            .ToListAsync();

        // Batch load booking counts in range (non-cancelled/expired)
        var bookingCounts = await _db.Bookings
            .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to &&
                         b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .GroupBy(b => new { b.Date, b.TimeSlot })
            .Select(g => new { g.Key.Date, g.Key.TimeSlot, Count = g.Count() })
            .ToListAsync();

        var result = new List<(DateOnly, TimeSlot, int, int, int)>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            foreach (var timeSlot in Enum.GetValues<TimeSlot>())
            {
                var isLocationBlocked = blockedDays.Any(b => b.Date == date && b.ParkingSlotId == null);
                if (isLocationBlocked)
                {
                    result.Add((date, timeSlot, 0, activeSlotCount, 0));
                    continue;
                }

                var blockedSlotIds = blockedDays
                    .Where(b => b.Date == date && b.ParkingSlotId != null)
                    .Select(b => b.ParkingSlotId!.Value)
                    .Where(id => activeSlotIds.Contains(id))
                    .Distinct().Count();

                var totalAvailable = activeSlotCount - blockedSlotIds;
                var booked = bookingCounts
                    .FirstOrDefault(b => b.Date == date && b.TimeSlot == timeSlot)?.Count ?? 0;

                var available = Math.Max(0, totalAvailable - booked);
                result.Add((date, timeSlot, available, totalAvailable, booked));
            }
        }
        return result;
    }
}
