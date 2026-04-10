namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Api.DTOs.Grid;
using SchulerPark.Api.DTOs.Location;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationController : ControllerBase
{
    private readonly ILocationService _locationService;
    private readonly AppDbContext _db;

    public LocationController(ILocationService locationService, AppDbContext db)
    {
        _locationService = locationService;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<LocationDto>>> GetLocations()
    {
        var locations = await _locationService.GetActiveLocationsAsync();
        var dtos = locations.Select(l => new LocationDto(
            l.Id, l.Name, l.Address, l.ParkingSlots.Count)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}/slots")]
    public async Task<ActionResult<List<ParkingSlotDto>>> GetSlots(Guid id)
    {
        var slots = await _locationService.GetLocationSlotsAsync(id);
        var dtos = slots.Select(s => new ParkingSlotDto(
            s.Id, s.SlotNumber, s.Label, s.IsActive)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}/blocked-days")]
    public async Task<ActionResult<List<BlockedDayDto>>> GetBlockedDays(
        Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        var today = DateOnly.FromDateTime(berlinNow);
        var fromDate = from ?? today.AddDays(1);
        var toDate = to ?? fromDate.AddMonths(1);

        var blocked = await _locationService.GetBlockedDaysAsync(id, fromDate, toDate);
        var dtos = blocked.Select(b => new BlockedDayDto(
            b.Id, b.Date, b.ParkingSlotId, b.Reason)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}/availability")]
    public async Task<ActionResult<List<AvailabilityDto>>> GetAvailability(
        Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        var today = DateOnly.FromDateTime(berlinNow);
        var fromDate = from ?? today.AddDays(1);
        var toDate = to ?? fromDate.AddMonths(1);

        var availability = await _locationService.GetAvailabilityAsync(id, fromDate, toDate);
        var dtos = availability.Select(a => new AvailabilityDto(
            a.Date, a.TimeSlot.ToString(), a.Available, a.Total, a.Booked)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}/grid-availability")]
    public async Task<ActionResult<GridAvailabilityDto>> GetGridAvailability(
        Guid id, [FromQuery] DateOnly date, [FromQuery] string timeSlot)
    {
        if (!Enum.TryParse<TimeSlot>(timeSlot, ignoreCase: true, out var ts))
            return BadRequest(new ProblemDetails { Title = "Bad Request", Detail = "Invalid timeSlot.", Status = 400 });

        var location = await _db.Locations
            .Include(l => l.ParkingSlots)
            .Include(l => l.GridCells)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location == null) return NotFound();
        if (location.GridRows == null || location.GridColumns == null)
            return Ok(new GridAvailabilityDto(0, 0, [], []));

        var slotsOnGrid = location.ParkingSlots
            .Where(s => s.GridRow.HasValue && s.GridColumn.HasValue)
            .ToList();

        // Load bookings for this location/date/timeslot
        var activeStatuses = new[] { BookingStatus.Pending, BookingStatus.Won, BookingStatus.Confirmed };
        var bookings = await _db.Bookings
            .Where(b => b.LocationId == id && b.Date == date && b.TimeSlot == ts
                && activeStatuses.Contains(b.Status))
            .ToListAsync();

        var bookedSlotIds = bookings
            .Where(b => b.ParkingSlotId.HasValue)
            .Select(b => b.ParkingSlotId!.Value)
            .ToHashSet();

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ownSlotIds = bookings
            .Where(b => b.UserId == userId && b.ParkingSlotId.HasValue)
            .Select(b => b.ParkingSlotId!.Value)
            .ToHashSet();

        // Load blocked days
        var blockedDays = await _db.BlockedDays
            .Where(b => b.LocationId == id && b.Date == date)
            .ToListAsync();
        var locationWideBlock = blockedDays.Any(b => b.ParkingSlotId == null);
        var blockedSlotIds = blockedDays
            .Where(b => b.ParkingSlotId.HasValue)
            .Select(b => b.ParkingSlotId!.Value)
            .ToHashSet();

        var slotDtos = slotsOnGrid.Select(s =>
        {
            string status;
            if (!s.IsActive)
                status = "Inactive";
            else if (locationWideBlock || blockedSlotIds.Contains(s.Id))
                status = "Blocked";
            else if (ownSlotIds.Contains(s.Id))
                status = "Own";
            else if (bookedSlotIds.Contains(s.Id))
                status = "Booked";
            else
                status = "Free";

            return new GridAvailabilitySlotDto(
                s.Id, s.SlotNumber, s.Label, s.GridRow!.Value, s.GridColumn!.Value, status);
        }).ToList();

        var cellDtos = location.GridCells
            .OrderBy(c => c.Row).ThenBy(c => c.Column)
            .Select(c => new GridCellDto(c.Id, c.Row, c.Column, c.CellType.ToString(), c.Label))
            .ToList();

        return Ok(new GridAvailabilityDto(
            location.GridRows.Value, location.GridColumns.Value, slotDtos, cellDtos));
    }
}
