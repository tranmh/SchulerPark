namespace SchulerPark.Api.DTOs.Profile;

public record DataExportDto(
    UserProfileExport Profile,
    List<BookingExport> Bookings,
    List<LotteryHistoryExport> LotteryHistory,
    DateTime ExportedAt);

public record UserProfileExport(
    string Email, string DisplayName, string? CarLicensePlate,
    string Role, DateTime CreatedAt);

public record BookingExport(
    Guid Id, string LocationName, DateOnly Date, string TimeSlot,
    string Status, string? ParkingSlotNumber, DateTime? ConfirmedAt, DateTime CreatedAt);

public record LotteryHistoryExport(
    string LocationName, DateOnly Date, string TimeSlot, bool Won);
