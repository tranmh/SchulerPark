namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Models;

public interface IBookingService
{
    Task<(Booking Booking, string? FallbackReason)> CreateBookingAsync(
        Guid userId, Guid? locationId, DateOnly date, TimeSlot timeSlot);
    Task<(List<Booking> Bookings, int TotalCount)> GetUserBookingsAsync(
        Guid userId, int page, int pageSize,
        BookingStatus? statusFilter = null, DateOnly? fromDate = null, DateOnly? toDate = null);
    Task<(List<(Booking Booking, string? FallbackReason)> Created,
          List<(DateOnly Date, string Reason)> Skipped)>
        CreateWeekBookingAsync(Guid userId, Guid? locationId, DateOnly weekStartDate, TimeSlot timeSlot);
    Task<Booking> CancelBookingAsync(Guid bookingId, Guid userId);
    Task<Booking> ConfirmBookingAsync(Guid bookingId, Guid userId);

    /// <summary>The bookable date window as the server sees it right now (WP3 3.1).</summary>
    BookingWindow GetBookingWindow();
}
