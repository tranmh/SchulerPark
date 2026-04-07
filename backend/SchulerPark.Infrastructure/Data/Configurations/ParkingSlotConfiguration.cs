namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class ParkingSlotConfiguration : IEntityTypeConfiguration<ParkingSlot>
{
    public void Configure(EntityTypeBuilder<ParkingSlot> builder)
    {
        builder.ToTable("ParkingSlots");
        builder.HasKey(ps => ps.Id);
        builder.Property(ps => ps.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(ps => ps.SlotNumber).IsRequired().HasMaxLength(20);
        builder.Property(ps => ps.Label).HasMaxLength(100);
        builder.Property(ps => ps.IsActive).HasDefaultValue(true);

        builder.HasOne(ps => ps.Location)
            .WithMany(l => l.ParkingSlots)
            .HasForeignKey(ps => ps.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ps => new { ps.LocationId, ps.SlotNumber }).IsUnique();
    }
}
