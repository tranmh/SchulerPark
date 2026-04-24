namespace SchulerPark.Infrastructure.Services.Placement;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;

public class ManhattanDistanceMetric : ISlotDistanceMetric
{
    public int Distance(ParkingSlot from, ParkingSlot to, Location location, IReadOnlyList<GridCell> cells)
    {
        if (from.GridRow is null || from.GridColumn is null
            || to.GridRow is null || to.GridColumn is null)
            return int.MaxValue;

        return Math.Abs(from.GridRow.Value - to.GridRow.Value)
             + Math.Abs(from.GridColumn.Value - to.GridColumn.Value);
    }
}
