namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class LotteryRun
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public DateOnly Date { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public LotteryAlgorithm Algorithm { get; set; }
    public DateTime RanAt { get; set; }
    public int TotalBookings { get; set; }
    public int AvailableSlots { get; set; }

    // Navigation properties
    public Location Location { get; set; } = null!;
}
