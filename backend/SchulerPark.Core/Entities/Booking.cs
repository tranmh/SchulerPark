namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class Booking
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParkingSlotId { get; set; }
    public Guid LocationId { get; set; }
    public DateOnly Date { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public ParkingSlot? ParkingSlot { get; set; }
    public Location Location { get; set; } = null!;
}
