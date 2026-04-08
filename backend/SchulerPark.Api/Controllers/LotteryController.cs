namespace SchulerPark.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;

[ApiController]
[Route("api/lottery")]
[Authorize(Policy = "AdminOnly")]
public class LotteryController : ControllerBase
{
    private readonly ILotteryService _lotteryService;

    public LotteryController(ILotteryService lotteryService)
    {
        _lotteryService = lotteryService;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunAll([FromQuery] DateOnly date)
    {
        await _lotteryService.RunAllLotteriesAsync(date);
        return Ok(new { message = $"Lottery completed for {date}" });
    }

    [HttpPost("run/{locationId:guid}")]
    public async Task<IActionResult> RunForSlot(
        Guid locationId,
        [FromQuery] DateOnly date,
        [FromQuery] string timeSlot)
    {
        if (!Enum.TryParse<TimeSlot>(timeSlot, ignoreCase: true, out var ts))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid time slot. Must be 'Morning' or 'Afternoon'.",
                Status = 400
            });

        await _lotteryService.RunLotteryForSlotAsync(locationId, date, ts);
        return Ok(new { message = $"Lottery completed for {locationId} on {date} {ts}" });
    }
}
