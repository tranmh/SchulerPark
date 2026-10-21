namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data.Migrations;
using Xunit;

/// <summary>
/// Data migration that replaces the Goeppingen grid with the site plan (two rows of spots
/// along one lane). Builds the old 8 × 12 state and runs the exact SQL the migration executes.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class GoeppingenSiteLayoutMigrationTests
{
    private readonly PostgresFixture _fx;
    public GoeppingenSiteLayoutMigrationTests(PostgresFixture fx) => _fx = fx;

    private static readonly int[] Numbers = [.. Enumerable.Range(97, 14), .. Enumerable.Range(122, 14)];

    private static readonly (int Row, int Col)[] OldPositions =
    [
        (1, 1), (1, 2), (1, 4), (1, 5), (1, 7), (1, 8), (1, 10),
        (2, 1), (2, 2), (2, 4), (2, 5), (2, 7), (2, 8), (2, 10),
        (4, 1), (4, 2), (4, 4), (4, 5), (4, 7), (4, 8), (4, 10),
        (5, 1), (5, 2), (5, 4), (5, 5), (5, 7), (5, 8), (5, 10),
    ];

    private async Task<(Guid Goeppingen, Guid OperatorSlot, Guid Other)> SeedOldStateAsync()
    {
        await using var db = _fx.NewContext();

        var goeppingen = new Location { Id = Guid.NewGuid(), Name = "Goeppingen", GridRows = 8, GridColumns = 12 };
        var other = new Location { Id = Guid.NewGuid(), Name = $"Other-{Guid.NewGuid():N}", GridRows = 8, GridColumns = 12 };
        db.Locations.AddRange(goeppingen, other);

        for (var i = 0; i < Numbers.Length; i++)
        {
            db.ParkingSlots.Add(new ParkingSlot
            {
                Id = Guid.NewGuid(), LocationId = goeppingen.Id, SlotNumber = $"P{Numbers[i]:D3}",
                IsActive = true, GridRow = OldPositions[i].Row, GridColumn = OldPositions[i].Col
            });
        }
        var operatorSlot = new ParkingSlot
        {
            Id = Guid.NewGuid(), LocationId = goeppingen.Id, SlotNumber = "E-1", IsActive = true, GridRow = 7, GridColumn = 0
        };
        db.ParkingSlots.Add(operatorSlot);
        foreach (var col in new[] { 3, 6, 9 })
            db.GridCells.Add(new GridCell { Id = Guid.NewGuid(), LocationId = goeppingen.Id, Row = 1, Column = col, CellType = GridCellType.Road });
        db.GridCells.Add(new GridCell { Id = Guid.NewGuid(), LocationId = other.Id, Row = 1, Column = 1, CellType = GridCellType.Road });

        await db.SaveChangesAsync();
        return (goeppingen.Id, operatorSlot.Id, other.Id);
    }

    [SkippableFact]
    public async Task Up_LaysOutTwoRowsAlongOneLane_Idempotent()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var (g, operatorSlot, other) = await SeedOldStateAsync();
        try
        {
            for (var run = 0; run < 2; run++)
            {
                await using (var db = _fx.NewContext())
                    await db.Database.ExecuteSqlRawAsync(GoeppingenSiteLayout.UpSql);

                await using var check = _fx.NewContext();
                var location = await check.Locations.SingleAsync(l => l.Id == g);
                Assert.Equal((15, 3), (location.GridRows, location.GridColumns));

                var slots = await check.ParkingSlots.Where(p => p.LocationId == g).ToDictionaryAsync(p => p.SlotNumber);
                Assert.Equal((1, 0), (slots["P110"].GridRow, slots["P110"].GridColumn));
                Assert.Equal((14, 0), (slots["P097"].GridRow, slots["P097"].GridColumn));
                Assert.Equal((1, 2), (slots["P135"].GridRow, slots["P135"].GridColumn));
                Assert.Equal((14, 2), (slots["P122"].GridRow, slots["P122"].GridColumn));
                Assert.Null(slots["E-1"].GridRow);
                Assert.Equal(operatorSlot, slots["E-1"].Id);

                var cells = await check.GridCells.Where(c => c.LocationId == g).ToListAsync();
                Assert.Equal(15, cells.Count);
                Assert.Equal(14, cells.Count(c => c.CellType == GridCellType.Road && c.Column == 1));
                var label = Assert.Single(cells, c => c.CellType == GridCellType.Label);
                Assert.Equal((0, 1, "Ladesäulen"), (label.Row, label.Column, label.Label));

                Assert.Equal(1, await check.GridCells.CountAsync(c => c.LocationId == other));
            }
        }
        finally
        {
            await using var db = _fx.NewContext();
            await db.Locations.Where(l => l.Id == g || l.Id == other).ExecuteDeleteAsync();
        }
    }

    [SkippableFact]
    public async Task Down_RestoresPreviousLayout()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var (g, _, other) = await SeedOldStateAsync();
        try
        {
            await using (var db = _fx.NewContext())
            {
                await db.Database.ExecuteSqlRawAsync(GoeppingenSiteLayout.UpSql);
                await db.Database.ExecuteSqlRawAsync(GoeppingenSiteLayout.DownSql);
            }

            await using var check = _fx.NewContext();
            var location = await check.Locations.SingleAsync(l => l.Id == g);
            Assert.Equal((8, 12), (location.GridRows, location.GridColumns));
            var slots = await check.ParkingSlots.Where(p => p.LocationId == g).ToDictionaryAsync(p => p.SlotNumber);
            for (var i = 0; i < Numbers.Length; i++)
                Assert.Equal(OldPositions[i], (slots[$"P{Numbers[i]:D3}"].GridRow!.Value, slots[$"P{Numbers[i]:D3}"].GridColumn!.Value));
            Assert.Equal(12 + 15 + 4 + 1 + 3, await check.GridCells.CountAsync(c => c.LocationId == g));
        }
        finally
        {
            await using var db = _fx.NewContext();
            await db.Locations.Where(l => l.Id == g || l.Id == other).ExecuteDeleteAsync();
        }
    }
}
