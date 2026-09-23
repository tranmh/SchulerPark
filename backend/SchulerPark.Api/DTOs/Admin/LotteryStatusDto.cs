namespace SchulerPark.Api.DTOs.Admin;

/// <summary>
/// One location × time slot for <c>GET /api/admin/lottery/status?date=</c> (WP1 3.5).
/// <c>Status</c> is <c>ran</c>, <c>not_run</c> (Pending bookings but no run) or <c>no_demand</c>.
/// </summary>
public record LotteryStatusDto(
    Guid LocationId,
    string LocationName,
    string TimeSlot,
    int PendingCount,
    DateTime? RanAt,
    int? TotalBookings,
    int? AvailableSlots,
    string Status);
