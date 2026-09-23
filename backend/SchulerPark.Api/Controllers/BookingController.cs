namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchulerPark.Api.DTOs.Booking;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingController : ControllerBase
{
    private readonly IBookingService _bookingService;
    private readonly IWaitlistService _waitlistService;
    private readonly IEmailService _emailService;

    public BookingController(IBookingService bookingService, IWaitlistService waitlistService, IEmailService emailService)
    {
        _bookingService = bookingService;
        _waitlistService = waitlistService;
        _emailService = emailService;
    }

    /// <summary>WP3 3.1: the bookable window as the server computes it, so the calendar matches the validation.</summary>
    [HttpGet("window")]
    public ActionResult<BookingWindowDto> GetWindow()
    {
        var w = _bookingService.GetBookingWindow();
        return Ok(new BookingWindowDto(w.Today, w.MinDate, w.MaxDate, w.MaxDaysAhead, w.MorningOpenToday, w.AfternoonOpenToday,
            w.LotteryTime, w.MorningDeadline, w.AfternoonDeadline));
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create([FromBody] CreateBookingRequest request)
    {
        if (!Enum.TryParse<TimeSlot>(request.TimeSlot, ignoreCase: true, out var timeSlot) || !Enum.IsDefined(timeSlot))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid time slot. Must be 'Morning' or 'Afternoon'.",
                Status = 400
            });

        var (booking, fallbackReason) = await _bookingService.CreateBookingAsync(
            GetUserId(), request.LocationId, request.Date, timeSlot);

        // Directly-assigned (Confirmed) and Waitlisted bookings get their own
        // notifications from BookingService; the "lottery tonight" created email is
        // only accurate for Pending bookings.
        if (booking.Status == BookingStatus.Pending)
            _ = _emailService.SendBookingCreatedAsync(booking);

        var dto = ToBookingDto(booking, fallbackReason);
        return CreatedAtAction(nameof(GetMyBookings), dto);
    }

    [HttpPost("week")]
    public async Task<ActionResult<WeekBookingResponse>> CreateWeek([FromBody] CreateWeekBookingRequest request)
    {
        if (!Enum.TryParse<TimeSlot>(request.TimeSlot, ignoreCase: true, out var timeSlot) || !Enum.IsDefined(timeSlot))
            return BadRequest(new ProblemDetails
            {
                Title = "Bad Request",
                Detail = "Invalid time slot. Must be 'Morning' or 'Afternoon'.",
                Status = 400
            });

        var (created, skipped) = await _bookingService.CreateWeekBookingAsync(
            GetUserId(), request.LocationId, request.WeekStartDate, timeSlot);

        foreach (var (booking, _) in created)
        {
            if (booking.Status == BookingStatus.Pending)
                _ = _emailService.SendBookingCreatedAsync(booking);
        }

        return Ok(new WeekBookingResponse(
            created.Select(c => ToBookingDto(c.Booking, c.FallbackReason)).ToList(),
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
            if (!Enum.TryParse<BookingStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
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

        // WP4 2.7: approximate queue position for waitlisted rows.
        var positions = await _waitlistService.GetWaitlistPositionsAsync(bookings);
        var dtos = bookings
            .Select(b => ToBookingDto(b) with { WaitlistPosition = positions.TryGetValue(b.Id, out var p) ? p : null })
            .ToList();
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

    private static BookingDto ToBookingDto(Core.Entities.Booking b, string? fallbackReason = null) => new(
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
        // WP4: the stored deadline (set when the booking became Won), never recomputed.
        ConfirmationDeadline: b.Status == BookingStatus.Won ? b.ConfirmationDeadline : null,
        FallbackReason: fallbackReason);
}
