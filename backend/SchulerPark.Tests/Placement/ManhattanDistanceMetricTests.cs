namespace SchulerPark.Tests.Placement;

using FluentAssertions;
using SchulerPark.Core.Entities;
using SchulerPark.Infrastructure.Services.Placement;

public class ManhattanDistanceMetricTests
{
    private readonly ManhattanDistanceMetric _metric = new();
    private readonly Location _location = new() { Id = Guid.NewGuid(), GridRows = 10, GridColumns = 10 };

    private static ParkingSlot Slot(int? r, int? c) => new()
    {
        Id = Guid.NewGuid(),
        SlotNumber = $"R{r}C{c}",
        GridRow = r,
        GridColumn = c,
        IsActive = true
    };

    [Fact]
    public void SameCell_IsZero()
    {
        var a = Slot(2, 3);
        _metric.Distance(a, a, _location, []).Should().Be(0);
    }

    [Fact]
    public void SameRow_IsColumnDelta()
    {
        _metric.Distance(Slot(1, 1), Slot(1, 5), _location, []).Should().Be(4);
    }

    [Fact]
    public void SameColumn_IsRowDelta()
    {
        _metric.Distance(Slot(2, 3), Slot(7, 3), _location, []).Should().Be(5);
    }

    [Fact]
    public void Diagonal_IsSumOfDeltas()
    {
        _metric.Distance(Slot(1, 1), Slot(4, 5), _location, []).Should().Be(7);
    }

    [Fact]
    public void MissingCoordinates_ReturnMaxValue()
    {
        _metric.Distance(Slot(null, 1), Slot(2, 3), _location, []).Should().Be(int.MaxValue);
        _metric.Distance(Slot(2, 3), Slot(1, null), _location, []).Should().Be(int.MaxValue);
    }
}
