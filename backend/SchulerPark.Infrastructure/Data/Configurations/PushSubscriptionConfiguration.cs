namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");
        builder.HasKey(ps => ps.Id);
        builder.Property(ps => ps.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(ps => ps.Endpoint).IsRequired().HasMaxLength(2048);
        builder.Property(ps => ps.P256dh).IsRequired().HasMaxLength(256);
        builder.Property(ps => ps.Auth).IsRequired().HasMaxLength(256);
        builder.Property(ps => ps.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(ps => ps.User)
            .WithMany(u => u.PushSubscriptions)
            .HasForeignKey(ps => ps.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ps => new { ps.UserId, ps.Endpoint }).IsUnique();
    }
}
