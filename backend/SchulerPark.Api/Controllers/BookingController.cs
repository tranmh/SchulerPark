namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchulerPark.Api.DTOs.Booking;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingController : ControllerBase
{
    private readonly IBookingService _bookingService;
    private readonly IEmailService _emailService;

    public BookingController(IBookingService bookingService, IEmailService emailService)
    {
        _bookingService = bookingService;
        _emailService = emailService;
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create([FromBody] CreateBookingRequest request)
    {
        if (!Enum.TryParse<TimeSlot>(request.TimeSlot, ignoreCase: true, out var timeSlot))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid time slot. Must be 'Morning' or 'Afternoon'.",
                Status = 400
            });

        var booking = await _bookingService.CreateBookingAsync(
            GetUserId(), request.LocationId, request.Date, timeSlot);

        _ = _emailService.SendBookingCreatedAsync(booking);

        var dto = ToBookingDto(booking);
        return CreatedAtAction(nameof(GetMyBookings), dto);
    }

    [HttpPost("week")]
    public async Task<ActionResult<WeekBookingResponse>> CreateWeek([FromBody] CreateWeekBookingRequest request)
    {
        if (!Enum.TryParse<TimeSlot>(request.TimeSlot, ignoreCase: true, out var timeSlot))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid time slot. Must be 'Morning' or 'Afternoon'.",
                Status = 400
            });

        var (created, skipped) = await _bookingService.CreateWeekBookingAsync(
            GetUserId(), request.LocationId, request.WeekStartDate, timeSlot);

        foreach (var booking in created)
            _ = _emailService.SendBookingCreatedAsync(booking);

        return Ok(new WeekBookingResponse(
            created.Select(ToBookingDto).ToList(),
            skipped.Select(s => new SkippedDay(s.Date, s.Reason)).ToList()));
    }

    [HttpGet("my")]
    public async Task<ActionResult<MyBookingsResponse>> GetMyBookings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        BookingStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<BookingStatus>(status, ignoreCase: true, out var parsed))
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = "Invalid status filter.",
                    Status = 400
                });
            statusFilter = parsed;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (bookings, totalCount) = await _bookingService.GetUserBookingsAsync(
            GetUserId(), page, pageSize, statusFilter, from, to);

        var dtos = bookings.Select(ToBookingDto).ToList();
        return Ok(new MyBookingsResponse(dtos, totalCount, page, pageSize));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var booking = await _bookingService.CancelBookingAsync(id, GetUserId());
        _ = _emailService.SendBookingCancelledAsync(booking);
        return NoContent();
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<BookingDto>> Confirm(Guid id)
    {
        var booking = await _bookingService.ConfirmBookingAsync(id, GetUserId());
        return Ok(ToBookingDto(booking));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static BookingDto ToBookingDto(Core.Entities.Booking b) => new(
        b.Id,
        b.LocationId,
        b.Location?.Name ?? "",
        b.ParkingSlotId,
        b.ParkingSlot?.SlotNumber,
        b.Date,
        b.TimeSlot.ToString(),
        b.Status.ToString(),
        b.ConfirmedAt,
        b.CreatedAt,
        ConfirmationDeadline: b.Status == BookingStatus.Won
            ? DeadlineHelper.GetConfirmationDeadline(b.Date, b.TimeSlot)
            : null);
}
