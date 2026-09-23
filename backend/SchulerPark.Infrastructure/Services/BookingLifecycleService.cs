namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Infrastructure.Data;

/// <summary>
/// Phase 20 WP1: the one place that turns "capacity or user went away" into booking state.
/// See <see cref="IBookingLifecycleService"/> for the contract.
/// </summary>
public class BookingLifecycleService : IBookingLifecycleService
{
    // WP4: Lost is terminal (day over), Waitlisted is the live "no slot yet" state.
    private static readonly BookingStatus[] LiveStatuses =
        [BookingStatus.Pending, BookingStatus.Won, BookingStatus.Confirmed, BookingStatus.Waitlisted];

    private readonly AppDbContext _db;
    private readonly IWaitlistService _waitlist;
    private readonly ISlotPlacer _placer;
    private readonly IEmailService _email;
    private readonly IPushNotificationService _push;
    private readonly TimeProvider _time;
    private readonly ILogger<BookingLifecycleService> _logger;

    public BookingLifecycleService(AppDbContext db, IWaitlistService waitlist, ISlotPlacer placer,
        IEmailService email, IPushNotificationService push, TimeProvider time,
        ILogger<BookingLifecycleService> logger)
    {
        _db = db;
        _waitlist = waitlist;
        _placer = placer;
        _email = email;
        _push = push;
        _time = time;
        _logger = logger;
    }

    public async Task<CapacityChangeResult> ReleaseUserBookingsAsync(Guid userId, BookingReleaseReason reason)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var today = DeadlineHelper.BerlinToday(now);

        var bookings = await _db.Bookings
            .Where(b => b.UserId == userId && b.Date >= today && LiveStatuses.Contains(b.Status))
            .ToListAsync();

        if (bookings.Count == 0)
            return CapacityChangeResult.Empty;

        var freed = new List<(Guid LocationId, DateOnly Date, TimeSlot TimeSlot, Guid SlotId)>();
        foreach (var booking in bookings)
        {
            if (booking.ParkingSlotId is { } slotId
                && (booking.Status == BookingStatus.Won || booking.Status == BookingStatus.Confirmed))
            {
                freed.Add((booking.LocationId, booking.Date, booking.TimeSlot, slotId));
            }
            Cancel(booking, actedBy: null, ReasonCode(reason), now);
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Released {Count} booking(s) of user {UserId} ({Reason}).",
            bookings.Count, userId, reason);

        // Slots are free now — hand them to waitlisters. Each promotion is its own save.
        foreach (var (locationId, date, timeSlot, slotId) in freed)
            await _waitlist.TryPromoteWaitlistAsync(locationId, date, timeSlot, slotId);

        return new CapacityChangeResult(bookings.Count, 0, 0, bookings.Count);
    }

    public async Task<CapacityChangeResult> HandleCapacityRemovedAsync(
        Guid locationId, DateOnly from, DateOnly? to, Guid? parkingSlotId,
        BookingReleaseReason reason, Guid actedByUserId, string? adminReason)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var query = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .Where(b => b.LocationId == locationId && b.Date >= from && LiveStatuses.Contains(b.Status));
        if (to.HasValue)
            query = query.Where(b => b.Date <= to.Value);

        if (parkingSlotId.HasValue)
        {
            // Slot-level removal only touches the bookings that hold that slot.
            var slotId = parkingSlotId.Value;
            query = query.Where(b => b.ParkingSlotId == slotId
                && (b.Status == BookingStatus.Won || b.Status == BookingStatus.Confirmed));
        }

        var affected = await query
            .OrderBy(b => b.Date).ThenBy(b => b.TimeSlot)
            // Confirmed users acted on their win; they get first pick of the remaining slots.
            .ThenBy(b => b.Status == BookingStatus.Confirmed ? 0 : 1)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync();

        if (affected.Count == 0)
            return CapacityChangeResult.Empty;

        var result = CapacityChangeResult.Empty;
        foreach (var group in affected.GroupBy(b => (b.Date, b.TimeSlot)))
        {
            var groupResult = parkingSlotId.HasValue
                ? await ReassignOrWaitlistAsync(locationId, group.Key.Date, group.Key.TimeSlot, [.. group], now)
                : await CancelAllAsync([.. group], actedByUserId, ReasonCode(reason), adminReason, now);
            result = result.Add(groupResult);
        }

        _logger.LogInformation(
            "Capacity removed at {LocationId} ({Reason}, slot {SlotId}, {From}..{To}): {Result}",
            locationId, reason, parkingSlotId, from, to, result);
        return result;
    }

    // Slot-level removal: try to move each displaced Won/Confirmed booking to a free slot,
    // otherwise it goes to the waitlist. One save per (date, timeSlot) group.
    private async Task<CapacityChangeResult> ReassignOrWaitlistAsync(
        Guid locationId, DateOnly date, TimeSlot timeSlot, List<Booking> displaced, DateTime now)
    {
        var location = await _db.Locations
            .Include(l => l.GridCells)
            .FirstAsync(l => l.Id == locationId);

        var displacedIds = displaced.Select(b => b.Id).ToHashSet();
        var previousSlots = displaced.ToDictionary(b => b.Id, b => b.ParkingSlot?.SlotNumber ?? "?");

        var reassigned = new List<Booking>();
        var waitlisted = new List<Booking>();

        async Task PlaceAllAsync()
        {
            reassigned.Clear();
            waitlisted.Clear();

            var free = await FreeSlotsAsync(locationId, date, timeSlot, displacedIds);
            var preferred = await PreferredSlotsAsync(displaced);
            var gridCells = location.GridCells.ToList();

            foreach (var booking in displaced)
            {
                booking.ParkingSlotId = null;
                booking.ParkingSlot = null;
                if (free.Count == 0)
                {
                    Waitlist(booking);
                    waitlisted.Add(booking);
                    continue;
                }

                var placement = _placer.Place([booking], free, location, gridCells, preferred);
                if (!placement.TryGetValue(booking.Id, out var newSlotId))
                {
                    Waitlist(booking);
                    waitlisted.Add(booking);
                    continue;
                }

                booking.ParkingSlotId = newSlotId;
                free.RemoveAll(s => s.Id == newSlotId);
                reassigned.Add(booking);
            }
        }

        await PlaceAllAsync();
        await BookingPersistence.SaveWithSlotConflictRetryAsync(_db, async () =>
        {
            // Someone grabbed one of "our" free slots concurrently: re-place everyone
            // against the committed state and try again.
            await PlaceAllAsync();
            return true;
        });

        foreach (var booking in reassigned)
        {
            await _db.Entry(booking).Reference(b => b.ParkingSlot).LoadAsync();
            var oldSlot = previousSlots[booking.Id];
            _ = _email.SendSlotReassignedAsync(booking, oldSlot);
            _ = _push.SendSlotReassignedAsync(booking, oldSlot);
        }
        foreach (var booking in waitlisted)
        {
            _ = _email.SendSlotWithdrawnAsync(booking);
            _ = _push.SendSlotWithdrawnAsync(booking);
        }

        return new CapacityChangeResult(displaced.Count, reassigned.Count, waitlisted.Count, 0);
    }

    // Whole-location removal: nobody parks, so every live booking is cancelled with the reason.
    private async Task<CapacityChangeResult> CancelAllAsync(
        List<Booking> bookings, Guid actedByUserId, string reasonCode, string? adminReason, DateTime now)
    {
        var reasonText = string.IsNullOrWhiteSpace(adminReason) ? reasonCode : adminReason.Trim();
        foreach (var booking in bookings)
            Cancel(booking, actedByUserId, reasonText, now);

        await _db.SaveChangesAsync();

        foreach (var booking in bookings)
        {
            _ = _email.SendBookingCancelledByAdminAsync(booking, adminReason);
            _ = _push.SendBookingCancelledByAdminAsync(booking, adminReason);
        }

        return new CapacityChangeResult(bookings.Count, 0, 0, bookings.Count);
    }

    // Unblocked active slots minus those held by a Won/Confirmed booking that is NOT being
    // displaced (the same set-minus as DirectAssignmentService.ApplyAsync).
    private async Task<List<ParkingSlot>> FreeSlotsAsync(
        Guid locationId, DateOnly date, TimeSlot timeSlot, HashSet<Guid> displacedIds)
    {
        var unblocked = await SlotAvailabilityHelper.GetUnblockedActiveSlotsAsync(_db, locationId, date);

        var occupied = await _db.Bookings
            .Where(b => b.LocationId == locationId && b.Date == date && b.TimeSlot == timeSlot
                && b.ParkingSlotId != null && !displacedIds.Contains(b.Id)
                && (b.Status == BookingStatus.Won || b.Status == BookingStatus.Confirmed))
            .Select(b => b.ParkingSlotId!.Value)
            .ToListAsync();
        var occupiedSet = occupied.ToHashSet();

        return unblocked.Where(s => !occupiedSet.Contains(s.Id)).ToList();
    }

    private async Task<Dictionary<Guid, ParkingSlot>> PreferredSlotsAsync(List<Booking> bookings)
    {
        var ids = bookings
            .Select(b => b.User?.PreferredSlotId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, ParkingSlot>();

        return (await _db.ParkingSlots.Where(s => ids.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id);
    }

    // Back to the waitlist: no slot, no confirmation, no deadline to expire against.
    private static void Waitlist(Booking booking)
    {
        booking.Status = BookingStatus.Waitlisted;
        booking.ConfirmedAt = null;
        booking.ConfirmationDeadline = null;
        booking.ReminderSentAt = null;
        booking.AutoConfirmReason = null;
    }

    private static void Cancel(Booking booking, Guid? actedBy, string reason, DateTime now)
    {
        booking.Status = BookingStatus.Cancelled;
        booking.ParkingSlotId = null;
        booking.ParkingSlot = null;
        booking.CancelledByUserId = actedBy;
        booking.CancelReason = reason;
        booking.CancelledAt = now;
    }

    /// <summary>snake_case code stored in <c>Booking.CancelReason</c> when no admin text is given.</summary>
    internal static string ReasonCode(BookingReleaseReason reason) => reason switch
    {
        BookingReleaseReason.UserDisabled => "user_disabled",
        BookingReleaseReason.UserDeleted => "user_deleted",
        BookingReleaseReason.AdminCancelled => "admin_cancelled",
        BookingReleaseReason.SlotBlocked => "slot_blocked",
        BookingReleaseReason.SlotDeactivated => "slot_deactivated",
        BookingReleaseReason.LocationBlocked => "location_blocked",
        BookingReleaseReason.LocationDeactivated => "location_deactivated",
        _ => reason.ToString()
    };
}
