namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

public class BookingService : IBookingService
{
    private static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private readonly AppDbContext _db;
    private readonly IWaitlistService _waitlistService;

    public BookingService(AppDbContext db, IWaitlistService waitlistService)
    {
        _db = db;
        _waitlistService = waitlistService;
    }

    public async Task<Booking> CreateBookingAsync(Guid userId, Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        // Date validation (Europe/Berlin timezone)
        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BerlinTz);
        var today = DateOnly.FromDateTime(berlinNow);
        if (date <= today)
            throw new ValidationException("Cannot book for today or past dates.");
        if (date > today.AddMonths(1))
            throw new ValidationException("Cannot book more than 1 month in advance.");

        // Location exists and is active
        var location = await _db.Locations
            .Include(l => l.ParkingSlots.Where(s => s.IsActive))
            .FirstOrDefaultAsync(l => l.Id == locationId && l.IsActive)
            ?? throw new NotFoundException("Location not found or inactive.");

        // At least one active slot
        if (location.ParkingSlots.Count == 0)
            throw new ValidationException("No active parking slots at this location.");

        // Location-wide block check
        var isLocationBlocked = await _db.BlockedDays.AnyAsync(b =>
            b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);
        if (isLocationBlocked)
            throw new ValidationException("This location is blocked on the selected date.");

        // Check if ALL individual slots are blocked
        var activeSlotIds = location.ParkingSlots.Select(s => s.Id).ToList();
        var blockedSlotCount = await _db.BlockedDays.CountAsync(b =>
            b.LocationId == locationId && b.Date == date &&
            b.ParkingSlotId != null && activeSlotIds.Contains(b.ParkingSlotId.Value));
        if (blockedSlotCount >= activeSlotIds.Count)
            throw new ValidationException("All parking slots are blocked on the selected date.");

        // Duplicate check
        var duplicate = await _db.Bookings.AnyAsync(b =>
            b.UserId == userId && b.LocationId == locationId &&
            b.Date == date && b.TimeSlot == timeSlot &&
            b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired);
        if (duplicate)
            throw new ValidationException("You already have a booking for this date, time slot, and location.");

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LocationId = locationId,
            Date = date,
            TimeSlot = timeSlot,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        // Eager-load navigations for DTO mapping and email
        await _db.Entry(booking).Reference(b => b.Location).LoadAsync();
        await _db.Entry(booking).Reference(b => b.User).LoadAsync();
        return booking;
    }

    public async Task<(List<Booking> Created, List<(DateOnly Date, string Reason)> Skipped)>
        CreateWeekBookingAsync(Guid userId, Guid locationId, DateOnly weekStartDate, TimeSlot timeSlot)
    {
        if (weekStartDate.DayOfWeek != DayOfWeek.Monday)
            throw new ValidationException("WeekStartDate must be a Monday.");

        var location = await _db.Locations
            .Include(l => l.ParkingSlots.Where(s => s.IsActive))
            .FirstOrDefaultAsync(l => l.Id == locationId && l.IsActive)
            ?? throw new NotFoundException("Location not found or inactive.");

        if (location.ParkingSlots.Count == 0)
            throw new ValidationException("No active parking slots at this location.");

        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BerlinTz);
        var today = DateOnly.FromDateTime(berlinNow);
        var activeSlotIds = location.ParkingSlots.Select(s => s.Id).ToList();

        var created = new List<Booking>();
        var skipped = new List<(DateOnly Date, string Reason)>();

        for (int i = 0; i < 5; i++)
        {
            var date = weekStartDate.AddDays(i);

            // Date validations (skip instead of throw)
            if (date <= today)
            {
                skipped.Add((date, "Cannot book for today or past dates."));
                continue;
            }
            if (date > today.AddMonths(1))
            {
                skipped.Add((date, "Cannot book more than 1 month in advance."));
                continue;
            }

            // Location-wide block
            var isBlocked = await _db.BlockedDays.AnyAsync(b =>
                b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);
            if (isBlocked)
            {
                skipped.Add((date, "Location is blocked on this date."));
                continue;
            }

            // All slots blocked
            var blockedSlotCount = await _db.BlockedDays.CountAsync(b =>
                b.LocationId == locationId && b.Date == date &&
                b.ParkingSlotId != null && activeSlotIds.Contains(b.ParkingSlotId.Value));
            if (blockedSlotCount >= activeSlotIds.Count)
            {
                skipped.Add((date, "All parking slots are blocked on this date."));
                continue;
            }

            // Duplicate check
            var duplicate = await _db.Bookings.AnyAsync(b =>
                b.UserId == userId && b.LocationId == locationId &&
                b.Date == date && b.TimeSlot == timeSlot &&
                b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired);
            if (duplicate)
            {
                skipped.Add((date, "You already have a booking for this date and time slot."));
                continue;
            }

            created.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LocationId = locationId,
                Date = date,
                TimeSlot = timeSlot,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (created.Count == 0)
            throw new ValidationException("No bookings could be created for the selected week. All days were skipped.");

        _db.Bookings.AddRange(created);
        await _db.SaveChangesAsync();

        // Eager-load navigations
        foreach (var booking in created)
        {
            await _db.Entry(booking).Reference(b => b.Location).LoadAsync();
            await _db.Entry(booking).Reference(b => b.User).LoadAsync();
        }

        return (created, skipped);
    }

    public async Task<(List<Booking> Bookings, int TotalCount)> GetUserBookingsAsync(
        Guid userId, int page, int pageSize,
        BookingStatus? statusFilter = null, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        var query = _db.Bookings
            .Where(b => b.UserId == userId)
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .AsQueryable();

        if (statusFilter.HasValue)
            query = query.Where(b => b.Status == statusFilter.Value);
        if (fromDate.HasValue)
            query = query.Where(b => b.Date >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(b => b.Date <= toDate.Value);

        var totalCount = await query.CountAsync();

        var bookings = await query
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (bookings, totalCount);
    }

    public async Task<Booking> CancelBookingAsync(Guid bookingId, Guid userId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Location)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new ForbiddenException("You can only cancel your own bookings.");

        if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Won
            && booking.Status != BookingStatus.Confirmed)
            throw new ValidationException("Only Pending, Won, or Confirmed bookings can be cancelled.");

        var freedSlotId = booking.ParkingSlotId;
        booking.Status = BookingStatus.Cancelled;
        booking.ParkingSlotId = null;
        await _db.SaveChangesAsync();

        // Promote highest-priority Lost booking if a slot was freed
        if (freedSlotId.HasValue)
        {
            await _waitlistService.TryPromoteWaitlistAsync(
                booking.LocationId, booking.Date, booking.TimeSlot, freedSlotId.Value);
        }

        return booking;
    }

    public async Task<Booking> ConfirmBookingAsync(Guid bookingId, Guid userId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new ForbiddenException("You can only confirm your own bookings.");

        if (booking.Status != BookingStatus.Won)
            throw new ValidationException("Only Won bookings can be confirmed.");

        if (DeadlineHelper.IsDeadlinePassed(booking.Date, booking.TimeSlot))
            throw new ValidationException("Confirmation deadline has passed.");

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return booking;
    }
}
