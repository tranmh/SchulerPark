namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class LotteryRunConfiguration : IEntityTypeConfiguration<LotteryRun>
{
    public void Configure(EntityTypeBuilder<LotteryRun> builder)
    {
        builder.ToTable("LotteryRuns");
        builder.HasKey(lr => lr.Id);
        builder.Property(lr => lr.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(lr => lr.Date).IsRequired();
        builder.Property(lr => lr.TimeSlot).HasConversion<string>().HasMaxLength(20);
        builder.Property(lr => lr.Algorithm).HasConversion<string>().HasMaxLength(30);
        builder.Property(lr => lr.RanAt).IsRequired();

        builder.HasOne(lr => lr.Location)
            .WithMany(l => l.LotteryRuns)
            .HasForeignKey(lr => lr.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(lr => new { lr.LocationId, lr.Date, lr.TimeSlot }).IsUnique();
    }
}
