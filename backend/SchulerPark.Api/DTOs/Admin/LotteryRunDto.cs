namespace SchulerPark.Api.DTOs.Admin;

public record LotteryRunDto(
    Guid Id, Guid LocationId, string LocationName,
    DateOnly Date, string TimeSlot, string Algorithm,
    DateTime RanAt, int TotalBookings, int AvailableSlots);
