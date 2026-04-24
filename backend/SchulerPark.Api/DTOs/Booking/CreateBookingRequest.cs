namespace SchulerPark.Api.DTOs.Booking;

public record CreateBookingRequest(Guid? LocationId, DateOnly Date, string TimeSlot);
