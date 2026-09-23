namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

public class BookingService : IBookingService
{
    // Cancelled rows persist for ~1 year (data retention), so an unbounded
    // cancel/rebook loop would grow the table without limit.
    private const int MaxBookingsCreatedPerUserPerDay = 50;

    private readonly AppDbContext _db;
    private readonly IWaitlistService _waitlistService;
    private readonly IDirectAssignmentService _directAssignment;
    private readonly IEmailService _emailService;
    private readonly IPushNotificationService _pushService;
    private readonly BookingSettings _settings;
    private readonly TimeProvider _time;

    public BookingService(AppDbContext db, IWaitlistService waitlistService,
        IDirectAssignmentService directAssignment, IEmailService emailService,
        IPushNotificationService pushService, IOptions<BookingSettings> settings,
        TimeProvider time)
    {
        _db = db;
        _waitlistService = waitlistService;
        _directAssignment = directAssignment;
        _emailService = emailService;
        _pushService = pushService;
        _settings = settings.Value;
        _time = time;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public BookingWindow GetBookingWindow()
    {
        var berlinNow = DeadlineHelper.ToBerlin(UtcNow);
        var today = DateOnly.FromDateTime(berlinNow);
        var morningOpen = DeadlineHelper.IsSameDayBookingOpen(today, TimeSlot.Morning, berlinNow);
        var afternoonOpen = DeadlineHelper.IsSameDayBookingOpen(today, TimeSlot.Afternoon, berlinNow);
        return new BookingWindow(
            today,
            MinDate: morningOpen || afternoonOpen ? today : today.AddDays(1),
            MaxDate: today.AddDays(_settings.MaxDaysAhead),
            _settings.MaxDaysAhead,
            morningOpen,
            afternoonOpen,
            LotteryTime: _settings.LotteryTimeOfDay.ToString("HH:mm"),
            MorningDeadline: _settings.MorningDeadline.ToString("HH:mm"),
            AfternoonDeadline: _settings.AfternoonDeadline.ToString("HH:mm"));
    }

    public async Task<(Booking Booking, string? FallbackReason)> CreateBookingAsync(
        Guid userId, Guid? locationId, DateOnly date, TimeSlot timeSlot)
    {
        // Date validation (Europe/Berlin timezone). WP3: today is bookable until the
        // slot ends (2.1); the horizon is Booking:MaxDaysAhead, the same number the
        // frontend reads from /api/bookings/window (3.1).
        var berlinNow = DeadlineHelper.ToBerlin(UtcNow);
        var today = DateOnly.FromDateTime(berlinNow);
        if (date < today)
            throw new ValidationException("Cannot book for past dates.", "booking_date_in_past");
        var isSameDay = date == today;
        if (isSameDay && !DeadlineHelper.IsSameDayBookingOpen(date, timeSlot, berlinNow))
            throw new ValidationException("This time slot has already ended today.", "booking_same_day_closed");
        if (date > today.AddDays(_settings.MaxDaysAhead))
            throw new ValidationException($"Cannot book more than {_settings.MaxDaysAhead} days in advance.", "booking_too_far_ahead");

        var createdToday = await _db.Bookings.CountAsync(b =>
            b.UserId == userId && b.CreatedAt >= UtcNow.AddDays(-1));
        if (createdToday >= MaxBookingsCreatedPerUserPerDay)
            throw new ValidationException("Daily booking creation limit reached. Please try again tomorrow.", "booking_daily_limit");

        var (resolvedLocationId, fallbackReason) = await ResolveLocationAsync(userId, locationId, date);

        await ValidateLocationForDateAsync(resolvedLocationId, date);

        await EnsureNoDuplicateAsync(userId, resolvedLocationId, date, timeSlot);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LocationId = resolvedLocationId,
            Date = date,
            TimeSlot = timeSlot,
            Status = BookingStatus.Pending,
            CreatedAt = UtcNow
        };

        // Same day: the lottery is history whether or not its row exists, so always assign
        // directly. A full day becomes a Waitlisted booking (WP4): a cancellation later in
        // the day hands the slot over as Confirmed, no confirmation step needed.
        var outcome = await _directAssignment.ApplyAsync(booking, assumeLotteryRan: isSameDay);

        _db.Bookings.Add(booking);
        outcome = await SaveWithSlotConflictRetryAsync(booking, outcome, isSameDay);

        await _db.Entry(booking).Reference(b => b.Location).LoadAsync();
        await _db.Entry(booking).Reference(b => b.User).LoadAsync();
        if (booking.ParkingSlotId.HasValue)
            await _db.Entry(booking).Reference(b => b.ParkingSlot).LoadAsync();

        SendDirectAssignmentNotifications(booking, outcome);
        return (booking, fallbackReason);
    }

    /// <summary>
    /// Saves pending changes; when the unique slot index rejects a direct
    /// assignment (another booking grabbed the slot concurrently), re-runs the
    /// assignment against the now-committed state and retries.
    /// </summary>
    private async Task<DirectAssignmentOutcome> SaveWithSlotConflictRetryAsync(
        Booking booking, DirectAssignmentOutcome outcome, bool isSameDay)
    {
        await BookingPersistence.SaveWithSlotConflictRetryAsync(_db, async () =>
        {
            if (outcome != DirectAssignmentOutcome.AssignedConfirmed)
                return false;

            booking.ParkingSlotId = null;
            booking.Status = BookingStatus.Pending;
            booking.ConfirmedAt = null;
            booking.AutoConfirmReason = null;
            outcome = await _directAssignment.ApplyAsync(booking, assumeLotteryRan: isSameDay);
            return true;
        });
        return outcome;
    }

    private void SendDirectAssignmentNotifications(Booking booking, DirectAssignmentOutcome outcome)
    {
        switch (outcome)
        {
            case DirectAssignmentOutcome.AssignedConfirmed:
                _ = _emailService.SendBookingDirectlyConfirmedAsync(booking);
                _ = _pushService.SendBookingDirectlyConfirmedAsync(booking);
                break;
            case DirectAssignmentOutcome.Waitlisted:
                _ = _emailService.SendBookingWaitlistedAsync(booking);
                _ = _pushService.SendBookingWaitlistedAsync(booking);
                break;
        }
    }

    public async Task<(List<(Booking Booking, string? FallbackReason)> Created,
                       List<(DateOnly Date, string Reason)> Skipped)>
        CreateWeekBookingAsync(Guid userId, Guid? locationId, DateOnly weekStartDate, TimeSlot timeSlot)
    {
        if (weekStartDate.DayOfWeek != DayOfWeek.Monday)
            throw new ValidationException("WeekStartDate must be a Monday.", "week_start_not_monday");

        var today = DeadlineHelper.BerlinToday(UtcNow);

        var created = new List<(Booking Booking, string? FallbackReason)>();
        var skipped = new List<(DateOnly Date, string Reason)>();
        var outcomes = new List<(Booking Booking, DirectAssignmentOutcome Outcome)>();

        // Bug #53: the week is all-or-nothing. Without an enclosing transaction the per-day
        // saves below commit independently, so a failure on (say) day 4 leaves days 1–3
        // committed and days 4–5 not — a partial week, with notifications already queued.
        // One transaction around the loop rolls the whole week back on any escape.
        // Safe because Program.cs uses plain UseNpgsql (no retrying execution strategy).
        await using var tx = await _db.Database.BeginTransactionAsync();

        for (int i = 0; i < 5; i++)
        {
            var date = weekStartDate.AddDays(i);

            // Week bookings stay tomorrow+: same-day slots are booked individually (2.1).
            if (date <= today)
            {
                skipped.Add((date, "Cannot book for today or past dates."));
                continue;
            }
            if (date > today.AddDays(_settings.MaxDaysAhead))
            {
                skipped.Add((date, $"Cannot book more than {_settings.MaxDaysAhead} days in advance."));
                continue;
            }

            Guid resolvedLocationId;
            string? fallbackReason;
            try
            {
                (resolvedLocationId, fallbackReason) =
                    await ResolveLocationAsync(userId, locationId, date);
            }
            catch (ValidationException ex)
            {
                skipped.Add((date, ex.Message));
                continue;
            }
            catch (NotFoundException ex)
            {
                skipped.Add((date, ex.Message));
                continue;
            }

            try
            {
                await ValidateLocationForDateAsync(resolvedLocationId, date);
                await EnsureNoDuplicateAsync(userId, resolvedLocationId, date, timeSlot);
            }
            catch (ValidationException ex)
            {
                skipped.Add((date, ex.Message));
                continue;
            }
            catch (NotFoundException ex)
            {
                skipped.Add((date, ex.Message));
                continue;
            }

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LocationId = resolvedLocationId,
                Date = date,
                TimeSlot = timeSlot,
                Status = BookingStatus.Pending,
                CreatedAt = UtcNow
            };
            var outcome = await _directAssignment.ApplyAsync(booking);
            _db.Bookings.Add(booking);
            // Per-day save so a slot conflict retry is attributable to this day.
            outcome = await SaveWithSlotConflictRetryAsync(booking, outcome, isSameDay: false);
            created.Add((booking, fallbackReason));
            outcomes.Add((booking, outcome));
        }

        if (created.Count == 0)
            throw new ValidationException("No bookings could be created for the selected week. All days were skipped.", "week_all_skipped");

        // All days that were going to be created succeeded — commit the whole week atomically.
        await tx.CommitAsync();

        foreach (var (booking, _) in created)
        {
            await _db.Entry(booking).Reference(b => b.Location).LoadAsync();
            await _db.Entry(booking).Reference(b => b.User).LoadAsync();
            if (booking.ParkingSlotId.HasValue)
                await _db.Entry(booking).Reference(b => b.ParkingSlot).LoadAsync();
        }

        foreach (var (booking, outcome) in outcomes)
            SendDirectAssignmentNotifications(booking, outcome);

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

        // WP4: leaving the waitlist is a cancel too.
        if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Won
            && booking.Status != BookingStatus.Confirmed && booking.Status != BookingStatus.Waitlisted)
            throw new ValidationException("Only Pending, Won, Confirmed or Waitlisted bookings can be cancelled.", "booking_not_cancellable");

        var freedSlotId = booking.Status == BookingStatus.Won || booking.Status == BookingStatus.Confirmed
            ? booking.ParkingSlotId
            : null;
        booking.Status = BookingStatus.Cancelled;
        booking.ParkingSlotId = null;
        booking.CancelledAt = UtcNow;
        await _db.SaveChangesAsync();

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
            throw new ValidationException("Only Won bookings can be confirmed.", "booking_not_confirmable");

        if (DeadlineHelper.IsDeadlinePassed(booking, UtcNow))
            throw new ValidationException("Confirmation deadline has passed.", "confirmation_deadline_passed");

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = UtcNow;
        await _db.SaveChangesAsync();

        return booking;
    }

    /// <summary>
    /// WP3 2.9: one live booking per user per date and time slot, at any location. A
    /// duplicate at the same location keeps its old code; at another location the
    /// message names it so the user knows what to cancel. Lost (day over) rows never
    /// collide with a new booking, since the date is in the past by then.
    /// </summary>
    private async Task EnsureNoDuplicateAsync(Guid userId, Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        var existing = await _db.Bookings
            .Where(b => b.UserId == userId && b.Date == date && b.TimeSlot == timeSlot &&
                        b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired
                        && b.Status != BookingStatus.Lost)
            .Select(b => new { b.LocationId, LocationName = b.Location.Name })
            .FirstOrDefaultAsync();
        if (existing == null)
            return;

        if (existing.LocationId == locationId)
            throw new ValidationException("You already have a booking for this date, time slot, and location.", "booking_duplicate");

        throw new ValidationException(
            $"You already have a booking for this date and time slot at '{existing.LocationName}'.",
            "booking_duplicate_other_location",
            new Dictionary<string, object?> { ["location"] = existing.LocationName });
    }

    private async Task<(Guid LocationId, string? FallbackReason)> ResolveLocationAsync(
        Guid userId, Guid? requestedLocationId, DateOnly date)
    {
        // Explicit client-provided location wins; no fallback.
        if (requestedLocationId.HasValue)
            return (requestedLocationId.Value, null);

        var user = await _db.Users.FindAsync(userId)
            ?? throw new NotFoundException("User not found.");

        if (!user.PreferredLocationId.HasValue)
            throw new ValidationException(
                "No location specified and no preferred location set. " +
                "Please choose a location or set a preferred one in your profile.", "no_preferred_location");

        var preferredId = user.PreferredLocationId.Value;
        var preferredReason = await GetUnavailabilityReasonAsync(preferredId, date);
        if (preferredReason == null)
            return (preferredId, null);

        // Preferred is unavailable — simple lottery over other active+available locations.
        var candidateIds = await _db.Locations
            .Where(l => l.IsActive && l.Id != preferredId)
            .Select(l => l.Id)
            .ToListAsync();

        var valid = new List<Guid>();
        foreach (var id in candidateIds)
        {
            if (await GetUnavailabilityReasonAsync(id, date) == null)
                valid.Add(id);
        }

        if (valid.Count == 0)
            throw new ValidationException(
                $"Your preferred location is unavailable on {date:yyyy-MM-dd} " +
                "and no alternative locations are available.", "preferred_location_unavailable");

        var picked = valid[Random.Shared.Next(valid.Count)];

        var preferredName = await _db.Locations
            .Where(l => l.Id == preferredId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync() ?? "preferred location";
        var pickedName = await _db.Locations
            .Where(l => l.Id == picked)
            .Select(l => l.Name)
            .FirstAsync();

        return (picked,
            $"Your preferred location '{preferredName}' is unavailable on {date:yyyy-MM-dd} " +
            $"({preferredReason}). Booked at '{pickedName}' instead.");
    }

    // Returns null if the location is bookable on `date`; else a short human reason.
    private async Task<string?> GetUnavailabilityReasonAsync(Guid locationId, DateOnly date)
    {
        var location = await _db.Locations
            .Include(l => l.ParkingSlots.Where(s => s.IsActive))
            .FirstOrDefaultAsync(l => l.Id == locationId);

        if (location == null || !location.IsActive)
            return "location is inactive";

        if (location.ParkingSlots.Count == 0)
            return "no active parking slots";

        var isLocationBlocked = await _db.BlockedDays.AnyAsync(b =>
            b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);
        if (isLocationBlocked)
            return "location blocked on this date";

        var activeSlotIds = location.ParkingSlots.Select(s => s.Id).ToList();
        var blockedSlotCount = await _db.BlockedDays.CountAsync(b =>
            b.LocationId == locationId && b.Date == date &&
            b.ParkingSlotId != null && activeSlotIds.Contains(b.ParkingSlotId.Value));
        if (blockedSlotCount >= activeSlotIds.Count)
            return "all slots blocked on this date";

        return null;
    }

    private async Task ValidateLocationForDateAsync(Guid locationId, DateOnly date)
    {
        var location = await _db.Locations
            .Include(l => l.ParkingSlots.Where(s => s.IsActive))
            .FirstOrDefaultAsync(l => l.Id == locationId && l.IsActive)
            ?? throw new NotFoundException("Location not found or inactive.");

        if (location.ParkingSlots.Count == 0)
            throw new ValidationException("No active parking slots at this location.", "location_no_active_slots");

        var isLocationBlocked = await _db.BlockedDays.AnyAsync(b =>
            b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);
        if (isLocationBlocked)
            throw new ValidationException("This location is blocked on the selected date.", "location_blocked");

        var activeSlotIds = location.ParkingSlots.Select(s => s.Id).ToList();
        var blockedSlotCount = await _db.BlockedDays.CountAsync(b =>
            b.LocationId == locationId && b.Date == date &&
            b.ParkingSlotId != null && activeSlotIds.Contains(b.ParkingSlotId.Value));
        if (blockedSlotCount >= activeSlotIds.Count)
            throw new ValidationException("All parking slots are blocked on the selected date.", "slots_all_blocked");
    }
}
