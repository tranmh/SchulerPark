namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

public class DirectAssignmentService : IDirectAssignmentService
{
    private readonly AppDbContext _db;
    private readonly ISlotPlacer _placer;
    private readonly ILogger<DirectAssignmentService> _logger;

    public DirectAssignmentService(AppDbContext db, ISlotPlacer placer,
        ILogger<DirectAssignmentService> logger)
    {
        _db = db;
        _placer = placer;
        _logger = logger;
    }

    public async Task<DirectAssignmentOutcome> ApplyAsync(Booking booking)
    {
        var lotteryRan = await _db.LotteryRuns.AnyAsync(lr =>
            lr.LocationId == booking.LocationId && lr.Date == booking.Date
            && lr.TimeSlot == booking.TimeSlot);
        if (!lotteryRan)
            return DirectAssignmentOutcome.NotApplicable;

        var unblockedSlots = await SlotAvailabilityHelper.GetUnblockedActiveSlotsAsync(
            _db, booking.LocationId, booking.Date);

        var occupiedSlotIds = await _db.Bookings
            .Where(b => b.LocationId == booking.LocationId && b.Date == booking.Date
                && b.TimeSlot == booking.TimeSlot && b.ParkingSlotId != null
                && (b.Status == BookingStatus.Won || b.Status == BookingStatus.Confirmed))
            .Select(b => b.ParkingSlotId!.Value)
            .ToListAsync();
        var occupied = occupiedSlotIds.ToHashSet();

        var freeSlots = unblockedSlots.Where(s => !occupied.Contains(s.Id)).ToList();
        if (freeSlots.Count == 0)
        {
            booking.Status = BookingStatus.Lost;
            booking.ParkingSlotId = null;
            _logger.LogInformation(
                "Direct assignment: no free slot for {LocationId} {Date} {TimeSlot}; booking {BookingId} waitlisted as Lost.",
                booking.LocationId, booking.Date, booking.TimeSlot, booking.Id);
            return DirectAssignmentOutcome.WaitlistedLost;
        }

        booking.User ??= (await _db.Users.FindAsync(booking.UserId))!;

        var location = await _db.Locations
            .Include(l => l.GridCells)
            .FirstAsync(l => l.Id == booking.LocationId);

        var preferredSlotId = booking.User.PreferredSlotId;
        var preferredSlots = new Dictionary<Guid, ParkingSlot>();
        if (preferredSlotId.HasValue)
        {
            var preferredSlot = freeSlots.FirstOrDefault(s => s.Id == preferredSlotId.Value)
                ?? await _db.ParkingSlots.FirstOrDefaultAsync(s => s.Id == preferredSlotId.Value);
            if (preferredSlot is not null)
                preferredSlots[preferredSlot.Id] = preferredSlot;
        }

        var placements = _placer.Place(
            new[] { booking }, freeSlots, location, location.GridCells.ToList(), preferredSlots);

        if (!placements.TryGetValue(booking.Id, out var slotId))
        {
            // Defensive: cannot happen with a non-empty free pool.
            booking.Status = BookingStatus.Lost;
            booking.ParkingSlotId = null;
            return DirectAssignmentOutcome.WaitlistedLost;
        }

        booking.ParkingSlotId = slotId;
        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = DateTime.UtcNow;
        _logger.LogInformation(
            "Direct assignment: booking {BookingId} confirmed on slot {SlotId} for {LocationId} {Date} {TimeSlot}.",
            booking.Id, slotId, booking.LocationId, booking.Date, booking.TimeSlot);
        return DirectAssignmentOutcome.AssignedConfirmed;
    }
}
