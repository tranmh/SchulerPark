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

        // Bug #18: Restrict (not SetNull) so a slot with live bookings can't be
        // hard-deleted into a slot-less "confirmed" booking. Decommission slots
        // via their IsActive flag instead.
        builder.HasOne(b => b.ParkingSlot)
            .WithMany(ps => ps.Bookings)
            .HasForeignKey(b => b.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Location)
            .WithMany(l => l.Bookings)
            .HasForeignKey(b => b.LocationId)
            // Bug #42: Restrict (not Cascade) — deleting a Location must NOT silently erase all
            // its bookings. Locations are decommissioned via IsActive=false; a genuine purge
            // must delete children explicitly first.
            .OnDelete(DeleteBehavior.Restrict);

        // One booking per user per date+timeslot+location (excluding cancelled)
        builder.HasIndex(b => new { b.UserId, b.Date, b.TimeSlot, b.LocationId })
            .IsUnique()
            .HasFilter("\"Status\" != 'Cancelled'");

        // Keep the plain FK index: the filtered composite below only contains
        // Won/Confirmed rows, so it cannot serve general ParkingSlotId lookups.
        builder.HasIndex(b => b.ParkingSlotId)
            .HasDatabaseName("IX_Bookings_ParkingSlotId");

        // A physical slot can be held by at most one active booking per date+timeslot
        // (backstop for concurrent direct assignments; not enforced by EF InMemory in tests)
        builder.HasIndex(b => new { b.ParkingSlotId, b.Date, b.TimeSlot })
            .IsUnique()
            .HasDatabaseName("IX_Bookings_ParkingSlotId_Date_TimeSlot")
            .HasFilter("\"ParkingSlotId\" IS NOT NULL AND \"Status\" IN ('Won', 'Confirmed')");
    }
}
