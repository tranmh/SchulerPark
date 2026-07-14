namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Infrastructure.Data;

public static class SlotAvailabilityHelper
{
    /// <summary>
    /// Active slots at the location minus BlockedDay blocks for the date
    /// (whole-location block => empty list; per-slot blocks excluded).
    /// </summary>
    public static async Task<List<ParkingSlot>> GetUnblockedActiveSlotsAsync(
        AppDbContext db, Guid locationId, DateOnly date)
    {
        var isLocationBlocked = await db.BlockedDays.AnyAsync(b =>
            b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);
        if (isLocationBlocked)
            return [];

        var blockedSlotIds = await db.BlockedDays
            .Where(b => b.LocationId == locationId && b.Date == date && b.ParkingSlotId != null)
            .Select(b => b.ParkingSlotId!.Value)
            .ToListAsync();

        return await db.ParkingSlots
            .Where(s => s.LocationId == locationId && s.IsActive
                && !blockedSlotIds.Contains(s.Id))
            .ToListAsync();
    }
}
