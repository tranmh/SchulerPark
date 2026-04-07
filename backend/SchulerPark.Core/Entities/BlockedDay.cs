namespace SchulerPark.Core.Entities;

public class BlockedDay
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public Guid? ParkingSlotId { get; set; }
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
    public Guid BlockedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Location Location { get; set; } = null!;
    public ParkingSlot? ParkingSlot { get; set; }
    public User BlockedByUser { get; set; } = null!;
}
