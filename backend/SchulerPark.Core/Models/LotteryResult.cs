namespace SchulerPark.Core.Models;

public record LotteryResult(Guid BookingId, Guid UserId, bool Won, Guid? AssignedSlotId);
