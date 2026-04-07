namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class LotteryHistoryConfiguration : IEntityTypeConfiguration<LotteryHistory>
{
    public void Configure(EntityTypeBuilder<LotteryHistory> builder)
    {
        builder.ToTable("LotteryHistories");
        builder.HasKey(lh => lh.Id);
        builder.Property(lh => lh.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(lh => lh.Date).IsRequired();
        builder.Property(lh => lh.TimeSlot).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(lh => lh.User)
            .WithMany(u => u.LotteryHistories)
            .HasForeignKey(lh => lh.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(lh => lh.Location)
            .WithMany(l => l.LotteryHistories)
            .HasForeignKey(lh => lh.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Performance index for WeightedHistory strategy queries
        builder.HasIndex(lh => new { lh.UserId, lh.LocationId, lh.Date });
    }
}
