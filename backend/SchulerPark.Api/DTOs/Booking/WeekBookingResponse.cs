namespace SchulerPark.Api.DTOs.Booking;

public record WeekBookingResponse(
    List<BookingDto> CreatedBookings,
    List<SkippedDay> SkippedDays);

public record SkippedDay(DateOnly Date, string Reason);
