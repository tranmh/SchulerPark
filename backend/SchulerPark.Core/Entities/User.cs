namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CarLicensePlate { get; set; }
    public string? AzureAdObjectId { get; set; }
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<LotteryHistory> LotteryHistories { get; set; } = [];
    public ICollection<BlockedDay> BlockedDays { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<PushSubscription> PushSubscriptions { get; set; } = [];
}
