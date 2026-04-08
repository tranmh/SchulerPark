namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Api.DTOs.Admin;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    // ── Locations ──────────────────────────────────────────────

    [HttpGet("locations")]
    public async Task<ActionResult<List<AdminLocationDto>>> GetLocations()
    {
        var locations = await _db.Locations
            .Include(l => l.ParkingSlots)
            .OrderBy(l => l.Name)
            .ToListAsync();

        var dtos = locations.Select(l => new AdminLocationDto(
            l.Id, l.Name, l.Address, l.IsActive,
            l.DefaultAlgorithm.ToString(),
            l.ParkingSlots.Count,
            l.ParkingSlots.Count(s => s.IsActive))).ToList();

        return Ok(dtos);
    }

    [HttpPost("locations")]
    public async Task<ActionResult<AdminLocationDto>> CreateLocation([FromBody] CreateLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Location name is required.");
        if (request.Name.Length > 100)
            throw new ValidationException("Location name must not exceed 100 characters.");
        if (string.IsNullOrWhiteSpace(request.Address))
            throw new ValidationException("Address is required.");
        if (request.Address.Length > 300)
            throw new ValidationException("Address must not exceed 300 characters.");

        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Address = request.Address,
            IsActive = true,
            DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
        };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetLocations), new AdminLocationDto(
            location.Id, location.Name, location.Address, location.IsActive,
            location.DefaultAlgorithm.ToString(), 0, 0));
    }

    [HttpPut("locations/{id:guid}")]
    public async Task<ActionResult<AdminLocationDto>> UpdateLocation(Guid id, [FromBody] UpdateLocationRequest request)
    {
        var location = await _db.Locations
            .Include(l => l.ParkingSlots)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (location == null) return NotFound();

        location.Name = request.Name;
        location.Address = request.Address;
        location.IsActive = request.IsActive;
        await _db.SaveChangesAsync();

        return Ok(new AdminLocationDto(
            location.Id, location.Name, location.Address, location.IsActive,
            location.DefaultAlgorithm.ToString(),
            location.ParkingSlots.Count,
            location.ParkingSlots.Count(s => s.IsActive)));
    }

    [HttpDelete("locations/{id:guid}")]
    public async Task<IActionResult> DeactivateLocation(Guid id)
    {
        var location = await _db.Locations.FindAsync(id);
        if (location == null) return NotFound();

        location.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("locations/{id:guid}/algorithm")]
    public async Task<IActionResult> SetAlgorithm(Guid id, [FromBody] SetAlgorithmRequest request)
    {
        if (!Enum.TryParse<LotteryAlgorithm>(request.Algorithm, ignoreCase: true, out var algorithm))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid algorithm. Must be 'PureRandom', 'WeightedHistory', or 'RoundRobin'.",
                Status = 400
            });

        var location = await _db.Locations.FindAsync(id);
        if (location == null) return NotFound();

        location.DefaultAlgorithm = algorithm;
        await _db.SaveChangesAsync();
        return Ok(new { algorithm = location.DefaultAlgorithm.ToString() });
    }

    // ── Slots ──────────────────────────────────────────────────

    [HttpGet("slots")]
    public async Task<ActionResult<List<AdminSlotDto>>> GetSlots([FromQuery] Guid locationId)
    {
        var slots = await _db.ParkingSlots
            .Where(s => s.LocationId == locationId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        return Ok(slots.Select(s => new AdminSlotDto(
            s.Id, s.LocationId, s.SlotNumber, s.Label, s.IsActive)).ToList());
    }

    [HttpPost("slots")]
    public async Task<ActionResult<AdminSlotDto>> CreateSlot([FromBody] CreateSlotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SlotNumber))
            throw new ValidationException("Slot number is required.");
        if (request.SlotNumber.Length > 20)
            throw new ValidationException("Slot number must not exceed 20 characters.");

        var locationExists = await _db.Locations.AnyAsync(l => l.Id == request.LocationId);
        if (!locationExists) throw new ValidationException("Location not found.");

        var slot = new ParkingSlot
        {
            Id = Guid.NewGuid(),
            LocationId = request.LocationId,
            SlotNumber = request.SlotNumber,
            Label = request.Label,
            IsActive = true
        };
        _db.ParkingSlots.Add(slot);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSlots), new { locationId = slot.LocationId },
            new AdminSlotDto(slot.Id, slot.LocationId, slot.SlotNumber, slot.Label, slot.IsActive));
    }

    [HttpPut("slots/{id:guid}")]
    public async Task<ActionResult<AdminSlotDto>> UpdateSlot(Guid id, [FromBody] UpdateSlotRequest request)
    {
        var slot = await _db.ParkingSlots.FindAsync(id);
        if (slot == null) return NotFound();

        slot.SlotNumber = request.SlotNumber;
        slot.Label = request.Label;
        slot.IsActive = request.IsActive;
        await _db.SaveChangesAsync();

        return Ok(new AdminSlotDto(slot.Id, slot.LocationId, slot.SlotNumber, slot.Label, slot.IsActive));
    }

    [HttpDelete("slots/{id:guid}")]
    public async Task<IActionResult> DeactivateSlot(Guid id)
    {
        var slot = await _db.ParkingSlots.FindAsync(id);
        if (slot == null) return NotFound();

        slot.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Blocked Days ───────────────────────────────────────────

    [HttpGet("blocked-days")]
    public async Task<ActionResult<List<AdminBlockedDayDto>>> GetBlockedDays(
        [FromQuery] Guid locationId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var query = _db.BlockedDays
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .Where(b => b.LocationId == locationId);

        if (from.HasValue) query = query.Where(b => b.Date >= from.Value);
        if (to.HasValue) query = query.Where(b => b.Date <= to.Value);

        var blocked = await query.OrderBy(b => b.Date).ToListAsync();

        return Ok(blocked.Select(b => new AdminBlockedDayDto(
            b.Id, b.LocationId, b.Location.Name,
            b.ParkingSlotId, b.ParkingSlot?.SlotNumber,
            b.Date, b.Reason, b.CreatedAt)).ToList());
    }

    [HttpPost("blocked-days")]
    public async Task<ActionResult<AdminBlockedDayDto>> CreateBlockedDay([FromBody] CreateBlockedDayRequest request)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
        if (request.Date < today)
            throw new ValidationException("Cannot block a date in the past.");

        var location = await _db.Locations.FindAsync(request.LocationId);
        if (location == null) throw new ValidationException("Location not found.");

        if (request.ParkingSlotId.HasValue)
        {
            var slotExists = await _db.ParkingSlots.AnyAsync(s => s.Id == request.ParkingSlotId.Value);
            if (!slotExists) throw new ValidationException("Parking slot not found.");
        }

        var blocked = new BlockedDay
        {
            Id = Guid.NewGuid(),
            LocationId = request.LocationId,
            ParkingSlotId = request.ParkingSlotId,
            Date = request.Date,
            Reason = request.Reason,
            BlockedByUserId = GetUserId(),
            CreatedAt = DateTime.UtcNow
        };
        _db.BlockedDays.Add(blocked);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBlockedDays), new { locationId = blocked.LocationId },
            new AdminBlockedDayDto(
                blocked.Id, blocked.LocationId, location.Name,
                blocked.ParkingSlotId, null,
                blocked.Date, blocked.Reason, blocked.CreatedAt));
    }

    [HttpDelete("blocked-days/{id:guid}")]
    public async Task<IActionResult> RemoveBlockedDay(Guid id)
    {
        var blocked = await _db.BlockedDays.FindAsync(id);
        if (blocked == null) return NotFound();

        _db.BlockedDays.Remove(blocked);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Bookings ───────────────────────────────────────────────

    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings(
        [FromQuery] Guid? locationId, [FromQuery] string? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(b => b.LocationId == locationId.Value);
        if (userId.HasValue)
            query = query.Where(b => b.UserId == userId.Value);
        if (from.HasValue)
            query = query.Where(b => b.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(b => b.Date <= to.Value);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<BookingStatus>(status, ignoreCase: true, out var s))
            query = query.Where(b => b.Status == s);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await query.CountAsync();
        var bookings = await query
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = bookings.Select(b => new AdminBookingDto(
            b.Id, b.UserId, b.User.Email, b.User.DisplayName,
            b.LocationId, b.Location.Name,
            b.ParkingSlotId, b.ParkingSlot?.SlotNumber,
            b.Date, b.TimeSlot.ToString(), b.Status.ToString(),
            b.ConfirmedAt, b.CreatedAt)).ToList();

        return Ok(new { bookings = dtos, totalCount, page, pageSize });
    }

    // ── Lottery Runs ───────────────────────────────────────────

    [HttpGet("lottery-runs")]
    public async Task<IActionResult> GetLotteryRuns(
        [FromQuery] Guid? locationId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.LotteryRuns
            .Include(lr => lr.Location)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(lr => lr.LocationId == locationId.Value);
        if (from.HasValue)
            query = query.Where(lr => lr.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(lr => lr.Date <= to.Value);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await query.CountAsync();
        var runs = await query
            .OrderByDescending(lr => lr.RanAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = runs.Select(lr => new LotteryRunDto(
            lr.Id, lr.LocationId, lr.Location.Name,
            lr.Date, lr.TimeSlot.ToString(), lr.Algorithm.ToString(),
            lr.RanAt, lr.TotalBookings, lr.AvailableSlots)).ToList();

        return Ok(new { lotteryRuns = dtos, totalCount, page, pageSize });
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
