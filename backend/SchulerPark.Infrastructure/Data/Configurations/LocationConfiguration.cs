namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Address).IsRequired().HasMaxLength(300);
        builder.Property(l => l.IsActive).HasDefaultValue(true);
        builder.Property(l => l.DefaultAlgorithm).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(l => l.Name).IsUnique();

        builder.Property(l => l.GridRows).IsRequired(false);
        builder.Property(l => l.GridColumns).IsRequired(false);
    }
}
