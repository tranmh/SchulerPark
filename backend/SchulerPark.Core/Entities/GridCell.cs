namespace SchulerPark.Core.Entities;

using SchulerPark.Core.Enums;

public class GridCell
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public GridCellType CellType { get; set; }
    public string? Label { get; set; }

    // Navigation properties
    public Location Location { get; set; } = null!;
}
