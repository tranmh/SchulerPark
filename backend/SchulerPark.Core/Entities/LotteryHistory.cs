namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class LotteryHistory
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid LocationId { get; set; }
    public DateOnly Date { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public bool Won { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
