namespace SchulerPark.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Api.DTOs.Admin;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/lottery")]
[Authorize(Policy = "AdminOnly")]
public class LotteryController : ControllerBase
{
    private readonly ILotteryService _lotteryService;
    private readonly AppDbContext _db;

    public LotteryController(ILotteryService lotteryService, AppDbContext db)
    {
        _lotteryService = lotteryService;
        _db = db;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunAll([FromQuery] DateOnly date)
    {
        var summary = await _lotteryService.RunAllLotteriesAsync(date);
        return Ok(new
        {
            message = summary.HasFailures
                ? $"Lottery for {date} completed with {summary.Failures.Count} failure(s)"
                : $"Lottery completed for {date}",
            succeeded = summary.Succeeded,
            failures = summary.Failures.Select(f => new
            {
                f.LocationId, f.LocationName, timeSlot = f.TimeSlot.ToString(), f.Error
            })
        });
    }

    [HttpPost("run/{locationId:guid}")]
    public async Task<IActionResult> RunForSlot(
        Guid locationId,
        [FromQuery] DateOnly date,
        [FromQuery] string timeSlot)
    {
        if (!Enum.TryParse<TimeSlot>(timeSlot, ignoreCase: true, out var ts) || !Enum.IsDefined(ts))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid time slot. Must be 'Morning' or 'Afternoon'.",
                Status = 400
            });

        await _lotteryService.RunLotteryForSlotAsync(locationId, date, ts);
        return Ok(new { message = $"Lottery completed for {locationId} on {date} {ts}" });
    }

    /// <summary>
    /// WP1 3.5: per active location × time slot, whether the lottery for <paramref name="date"/>
    /// ran and how much demand it had. Rows with Pending bookings and no run are the ones an
    /// admin has to act on (or the watchdog will).
    /// </summary>
    [HttpGet("/api/admin/lottery/status")]
    public async Task<ActionResult<List<LotteryStatusDto>>> Status([FromQuery] DateOnly date)
    {
        var locations = await _db.Locations
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name })
            .ToListAsync();

        var runs = await _db.LotteryRuns
            .Where(r => r.Date == date)
            .ToListAsync();

        var pending = await _db.Bookings
            .Where(b => b.Date == date && b.Status == BookingStatus.Pending)
            .GroupBy(b => new { b.LocationId, b.TimeSlot })
            .Select(g => new { g.Key.LocationId, g.Key.TimeSlot, Count = g.Count() })
            .ToListAsync();

        var rows = new List<LotteryStatusDto>();
        foreach (var location in locations)
        {
            foreach (var slot in Enum.GetValues<TimeSlot>())
            {
                var run = runs.FirstOrDefault(r => r.LocationId == location.Id && r.TimeSlot == slot);
                var pendingCount = pending
                    .FirstOrDefault(p => p.LocationId == location.Id && p.TimeSlot == slot)?.Count ?? 0;
                var status = run != null ? "ran" : pendingCount > 0 ? "not_run" : "no_demand";
                rows.Add(new LotteryStatusDto(
                    location.Id, location.Name, slot.ToString(), pendingCount,
                    run?.RanAt, run?.TotalBookings, run?.AvailableSlots, status));
            }
        }

        return Ok(rows);
    }
}
