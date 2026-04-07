namespace SchulerPark.Core.Entities;

public class ParkingSlot
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string SlotNumber { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Location Location { get; set; } = null!;
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<BlockedDay> BlockedDays { get; set; } = [];
}
