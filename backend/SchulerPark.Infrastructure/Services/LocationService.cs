namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
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

    /// <summary>
    /// WP3 3.2: "booked" means a slot is actually held (Won/Confirmed). Pending requests
    /// and waitlisted (Lost) bookings are reported separately so the UI can show demand
    /// before the lottery and free slots after it.
    /// </summary>
    public async Task<List<SlotAvailability>> GetAvailabilityAsync(Guid locationId, DateOnly from, DateOnly to)
    {
        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId && l.IsActive);
        if (!locationExists)
            throw new NotFoundException("Location not found or inactive.");

        var activeSlotIds = await _db.ParkingSlots
            .Where(s => s.LocationId == locationId && s.IsActive)
            .Select(s => s.Id)
            .ToListAsync();
        var activeSlotCount = activeSlotIds.Count;

        // Batch load blocked days in range
        var blockedDays = await _db.BlockedDays
            .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to)
            .ToListAsync();

        // Batch load per-status booking counts in range
        var statusCounts = await _db.Bookings
            .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to &&
                         b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .GroupBy(b => new { b.Date, b.TimeSlot, b.Status })
            .Select(g => new { g.Key.Date, g.Key.TimeSlot, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        var lotteryRuns = (await _db.LotteryRuns
            .Where(r => r.LocationId == locationId && r.Date >= from && r.Date <= to)
            .Select(r => new { r.Date, r.TimeSlot })
            .ToListAsync())
            .Select(r => (r.Date, r.TimeSlot))
            .ToHashSet();

        int Count(DateOnly date, TimeSlot slot, params BookingStatus[] statuses) =>
            statusCounts
                .Where(c => c.Date == date && c.TimeSlot == slot && statuses.Contains(c.Status))
                .Sum(c => c.Count);

        var result = new List<SlotAvailability>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var isLocationBlocked = blockedDays.Any(b => b.Date == date && b.ParkingSlotId == null);
            var blockedSlotCount = blockedDays
                .Where(b => b.Date == date && b.ParkingSlotId != null)
                .Select(b => b.ParkingSlotId!.Value)
                .Where(id => activeSlotIds.Contains(id))
                .Distinct().Count();

            foreach (var timeSlot in Enum.GetValues<TimeSlot>())
            {
                var lotteryRan = lotteryRuns.Contains((date, timeSlot));
                var pending = Count(date, timeSlot, BookingStatus.Pending);
                var waitlist = Count(date, timeSlot, BookingStatus.Waitlisted);
                var booked = Count(date, timeSlot, BookingStatus.Won, BookingStatus.Confirmed);

                if (isLocationBlocked)
                {
                    result.Add(new SlotAvailability(date, timeSlot, 0, 0, booked, pending, waitlist, lotteryRan));
                    continue;
                }

                var total = Math.Max(0, activeSlotCount - blockedSlotCount);
                var available = Math.Max(0, total - booked);
                result.Add(new SlotAvailability(date, timeSlot, available, total, booked, pending, waitlist, lotteryRan));
            }
        }
        return result;
    }
}
