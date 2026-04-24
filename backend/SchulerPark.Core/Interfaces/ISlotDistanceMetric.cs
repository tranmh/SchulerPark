namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface ISlotDistanceMetric
{
    int Distance(ParkingSlot from, ParkingSlot to, Location location, IReadOnlyList<GridCell> cells);
}
