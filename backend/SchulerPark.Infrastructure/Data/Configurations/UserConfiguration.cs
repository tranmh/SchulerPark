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
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        builder.Property(u => u.UpdatedAt).HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.AzureAdObjectId).IsUnique()
            .HasFilter("\"AzureAdObjectId\" is not null");
    }
}
