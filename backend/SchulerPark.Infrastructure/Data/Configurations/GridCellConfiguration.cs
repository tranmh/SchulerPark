namespace SchulerPark.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchulerPark.Core.Entities;

public class GridCellConfiguration : IEntityTypeConfiguration<GridCell>
{
    public void Configure(EntityTypeBuilder<GridCell> builder)
    {
        builder.ToTable("GridCells");
        builder.HasKey(gc => gc.Id);
        builder.Property(gc => gc.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(gc => gc.Row).IsRequired();
        builder.Property(gc => gc.Column).IsRequired();
        builder.Property(gc => gc.CellType).HasConversion<string>().HasMaxLength(20);
        builder.Property(gc => gc.Label).HasMaxLength(50);

        builder.HasOne(gc => gc.Location)
            .WithMany(l => l.GridCells)
            .HasForeignKey(gc => gc.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(gc => new { gc.LocationId, gc.Row, gc.Column }).IsUnique();
    }
}
