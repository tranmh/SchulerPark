namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class BlockedDayConfiguration : IEntityTypeConfiguration<BlockedDay>
{
    public void Configure(EntityTypeBuilder<BlockedDay> builder)
    {
        builder.ToTable("BlockedDays");
        builder.HasKey(bd => bd.Id);
        builder.Property(bd => bd.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(bd => bd.Date).IsRequired();
        builder.Property(bd => bd.Reason).HasMaxLength(500);
        builder.Property(bd => bd.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(bd => bd.Location)
            .WithMany(l => l.BlockedDays)
            .HasForeignKey(bd => bd.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(bd => bd.ParkingSlot)
            .WithMany(ps => ps.BlockedDays)
            .HasForeignKey(bd => bd.ParkingSlotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(bd => bd.BlockedByUser)
            .WithMany(u => u.BlockedDays)
            .HasForeignKey(bd => bd.BlockedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(bd => new { bd.LocationId, bd.Date });
    }
}
