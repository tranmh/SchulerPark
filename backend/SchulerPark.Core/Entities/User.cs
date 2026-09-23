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
    public bool EmailVerified { get; set; }
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; set; }
    /// <summary>SHA-256 of the single-use password-reset token (Phase 20, WP2); null when none is outstanding.</summary>
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Approved;
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? PreferredLocationId { get; set; }
    public Guid? PreferredSlotId { get; set; }
    /// <summary>"de" or "en" — language of emails and push notifications; follows the UI language last used.</summary>
    public string PreferredLanguage { get; set; } = Helpers.Localization.Default;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public Location? PreferredLocation { get; set; }
    public ParkingSlot? PreferredSlot { get; set; }
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<LotteryHistory> LotteryHistories { get; set; } = [];
    public ICollection<BlockedDay> BlockedDays { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<PushSubscription> PushSubscriptions { get; set; } = [];
}
