namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Enums;

public interface ILotteryService
{
    Task RunLotteryForSlotAsync(Guid locationId, DateOnly date, TimeSlot timeSlot);
    Task RunAllLotteriesAsync(DateOnly date);
}
