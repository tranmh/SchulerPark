namespace SchulerPark.Tests.Placement;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Services.Placement;

public class BfsDistanceMetricTests
{
    private readonly BfsDistanceMetric _metric = new();

    private static ParkingSlot Slot(int? r, int? c) => new()
    {
        Id = Guid.NewGuid(),
        SlotNumber = $"R{r}C{c}",
        GridRow = r,
        GridColumn = c,
        IsActive = true
    };

    private static GridCell Obstacle(int r, int c, Guid locId) => new()
    {
        Id = Guid.NewGuid(),
        LocationId = locId,
        Row = r,
        Column = c,
        CellType = GridCellType.Obstacle
    };

    [Fact]
    public void EmptyGrid_MatchesManhattan()
    {
        var loc = new Location { Id = Guid.NewGuid(), GridRows = 5, GridColumns = 5 };
        _metric.Distance(Slot(0, 0), Slot(3, 4), loc, []).Should().Be(7);
    }

    [Fact]
    public void Obstacles_ForceDetour()
    {
        // 3x3 grid with (0,1) and (1,1) blocked; path 0,0 -> 0,2 must detour.
        var loc = new Location { Id = Guid.NewGuid(), GridRows = 3, GridColumns = 3 };
        var cells = new List<GridCell>
        {
            Obstacle(0, 1, loc.Id),
            Obstacle(1, 1, loc.Id),
        };
        // Manhattan says 2, BFS must go 0,0 -> 1,0 -> 2,0 -> 2,1 -> 2,2 -> 1,2 -> 0,2 = 6.
        _metric.Distance(Slot(0, 0), Slot(0, 2), loc, cells).Should().Be(6);
    }

    [Fact]
    public void Unreachable_ReturnsMaxValue()
    {
        // Ring of obstacles isolates (1,1) from the rest.
        var loc = new Location { Id = Guid.NewGuid(), GridRows = 3, GridColumns = 3 };
        var cells = new List<GridCell>
        {
            Obstacle(0, 1, loc.Id),
            Obstacle(1, 0, loc.Id),
            Obstacle(1, 2, loc.Id),
            Obstacle(2, 1, loc.Id),
        };
        _metric.Distance(Slot(1, 1), Slot(0, 0), loc, cells).Should().Be(int.MaxValue);
    }

    [Fact]
    public void MissingCoordinates_ReturnMaxValue()
    {
        var loc = new Location { Id = Guid.NewGuid(), GridRows = 5, GridColumns = 5 };
        _metric.Distance(Slot(null, 0), Slot(1, 1), loc, []).Should().Be(int.MaxValue);
    }
}
