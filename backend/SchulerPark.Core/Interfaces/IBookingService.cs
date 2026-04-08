namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

public interface IBookingService
{
    Task<Booking> CreateBookingAsync(Guid userId, Guid locationId, DateOnly date, TimeSlot timeSlot);
    Task<(List<Booking> Bookings, int TotalCount)> GetUserBookingsAsync(
        Guid userId, int page, int pageSize,
        BookingStatus? statusFilter = null, DateOnly? fromDate = null, DateOnly? toDate = null);
    Task<Booking> CancelBookingAsync(Guid bookingId, Guid userId);
    Task<Booking> ConfirmBookingAsync(Guid bookingId, Guid userId);
}
