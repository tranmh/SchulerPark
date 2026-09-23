namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.CarLicensePlate).HasMaxLength(20);
        builder.Property(u => u.AzureAdObjectId).HasMaxLength(36);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        // Phase 20 WP2: SHA-256 hex of the outstanding reset token; looked up on reset.
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(64);
        builder.HasIndex(u => u.PasswordResetTokenHash)
            .HasFilter("\"PasswordResetTokenHash\" is not null");
        // Default 'de' so all pre-existing rows get German notifications, as before.
        builder.Property(u => u.PreferredLanguage).IsRequired().HasMaxLength(5)
            .HasDefaultValue(Core.Helpers.Localization.Default);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        // Default 'Approved' so all pre-Phase-18 rows stay usable after the migration.
        builder.Property(u => u.ApprovalStatus).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(Core.Enums.ApprovalStatus.Approved);
        builder.Property(u => u.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(u => u.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");

        // Bug #9: filter the unique Email index on live rows so a soft-deleted
        // account (DSGVO) doesn't block re-registration with the same email.
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("\"DeletedAt\" is null");
        builder.HasIndex(u => u.AzureAdObjectId).IsUnique()
            .HasFilter("\"AzureAdObjectId\" is not null");

        builder.HasOne(u => u.PreferredLocation)
            .WithMany()
            .HasForeignKey(u => u.PreferredLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(u => u.PreferredSlot)
            .WithMany()
            .HasForeignKey(u => u.PreferredSlotId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
