namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public LotteryAlgorithm DefaultAlgorithm { get; set; } = LotteryAlgorithm.WeightedHistory;
    public int? GridRows { get; set; }
    public int? GridColumns { get; set; }

    // Navigation properties
    public ICollection<ParkingSlot> ParkingSlots { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<LotteryRun> LotteryRuns { get; set; } = [];
    public ICollection<LotteryHistory> LotteryHistories { get; set; } = [];
    public ICollection<BlockedDay> BlockedDays { get; set; } = [];
    public ICollection<GridCell> GridCells { get; set; } = [];
}
