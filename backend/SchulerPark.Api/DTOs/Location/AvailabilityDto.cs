namespace SchulerPark.Api.DTOs.Location;

public record AvailabilityDto(DateOnly Date, string TimeSlot, int AvailableSlots, int TotalSlots, int BookingCount);
