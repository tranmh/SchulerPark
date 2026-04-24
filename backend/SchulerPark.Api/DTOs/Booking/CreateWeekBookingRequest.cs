namespace SchulerPark.Api.DTOs.Booking;

public record CreateWeekBookingRequest(Guid? LocationId, DateOnly WeekStartDate, string TimeSlot);
