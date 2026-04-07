namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(b => b.Date).IsRequired();
        builder.Property(b => b.TimeSlot).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.ParkingSlot)
            .WithMany(ps => ps.Bookings)
            .HasForeignKey(b => b.ParkingSlotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(b => b.Location)
            .WithMany(l => l.Bookings)
            .HasForeignKey(b => b.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // One booking per user per date+timeslot+location (excluding cancelled)
        builder.HasIndex(b => new { b.UserId, b.Date, b.TimeSlot, b.LocationId })
            .IsUnique()
            .HasFilter("\"Status\" != 'Cancelled'");
    }
}
