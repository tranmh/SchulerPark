namespace SchulerPark.Api.DTOs.Booking;

public record MyBookingsResponse(List<BookingDto> Bookings, int TotalCount, int Page, int PageSize);
