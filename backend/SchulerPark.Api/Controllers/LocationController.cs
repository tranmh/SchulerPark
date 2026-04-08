namespace SchulerPark.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchulerPark.Api.DTOs.Location;
using SchulerPark.Core.Interfaces;

[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationController(ILocationService locationService)
    {
        _locationService = locationService;
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
}
