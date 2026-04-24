namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface ISlotPlacer
{
    Dictionary<Guid, Guid> Place(
        IReadOnlyList<Booking> winners,
        IReadOnlyList<ParkingSlot> available,
        Location location,
        IReadOnlyList<GridCell> cells,
        IReadOnlyDictionary<Guid, ParkingSlot> preferredSlotsById);
}
