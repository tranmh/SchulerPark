namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data.Migrations;
using Xunit;

/// <summary>
/// Data migration that moves old-seed slot numbers (P001..Pnnn) to the physical numbering.
/// The fixture applies all migrations on an empty database, so each test builds the old-seed
/// state and runs the exact SQL the migration executes (exposed as constants on the class).
/// Locations are matched by name, so the test uses the real names and removes them afterwards.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class RenumberSeededParkingSlotsMigrationTests
{
    private readonly PostgresFixture _fx;
    public RenumberSeededParkingSlotsMigrationTests(PostgresFixture fx) => _fx = fx;

    private static readonly (int Row, int Col)[] OldGoeppingenPositions =
    [
        (1, 1), (1, 2), (1, 4), (1, 5), (1, 7), (1, 8), (1, 10),
        (2, 1), (2, 2), (2, 4), (2, 5), (2, 7), (2, 8), (2, 10),
        (4, 1), (4, 2), (4, 4), (4, 5), (4, 7), (4, 8),
    ];

    private sealed record Scenario(
        Guid Goeppingen, Guid Erfurt, Guid Hessdorf, Guid Other,
        Guid GoeppingenP005, Guid ErfurtP011, Guid ErfurtP012, Guid ErfurtP013, Guid ErfurtFree,
        Guid BookingOnGoeppingenP005, Guid UserPreferringErfurtP012, Guid UserPreferringGoeppingenP001);

    private static ParkingSlot Slot(Guid locationId, int no, int? row = null, int? col = null) => new()
    {
        Id = Guid.NewGuid(), LocationId = locationId, SlotNumber = $"P{no:D3}",
        Label = $"Parking Spot {no}", IsActive = true, GridRow = row, GridColumn = col
    };

    private async Task<Scenario> SeedOldStateAsync()
    {
        await using var db = _fx.NewContext();

        var goeppingen = new Location { Id = Guid.NewGuid(), Name = "Goeppingen", GridRows = 8, GridColumns = 12 };
        var erfurt = new Location { Id = Guid.NewGuid(), Name = "Erfurt", GridRows = 8, GridColumns = 10 };
        var hessdorf = new Location { Id = Guid.NewGuid(), Name = "Hessdorf" };
        var other = new Location { Id = Guid.NewGuid(), Name = $"Other-{Guid.NewGuid():N}" };
        db.Locations.AddRange(goeppingen, erfurt, hessdorf, other);

        var gSlots = Enumerable.Range(1, 20)
            .Select(i => Slot(goeppingen.Id, i, OldGoeppingenPositions[i - 1].Row, OldGoeppingenPositions[i - 1].Col))
            .ToList();
        db.ParkingSlots.AddRange(gSlots);
        // Roads in the lane row the new slots move into — must not be overwritten.
        foreach (var col in new[] { 3, 6, 9 })
            db.GridCells.Add(new GridCell { Id = Guid.NewGuid(), LocationId = goeppingen.Id, Row = 5, Column = col, CellType = GridCellType.Road });

        var eSlots = Enumerable.Range(1, 15).Select(i => Slot(erfurt.Id, i, 1, i - 1)).ToList();
        var erfurtFree = new ParkingSlot { Id = Guid.NewGuid(), LocationId = erfurt.Id, SlotNumber = "A-15", IsActive = true };
        db.ParkingSlots.AddRange(eSlots);
        db.ParkingSlots.Add(erfurtFree);

        db.ParkingSlots.AddRange(Enumerable.Range(1, 10).Select(i => Slot(hessdorf.Id, i)));
        db.ParkingSlots.AddRange(Enumerable.Range(1, 30).Select(i => Slot(other.Id, i)));

        var admin = new User { Id = Guid.NewGuid(), Email = $"a-{Guid.NewGuid():N}@x.de", DisplayName = "A" };
        var prefersErfurtP012 = new User { Id = Guid.NewGuid(), Email = $"p-{Guid.NewGuid():N}@x.de", DisplayName = "P", PreferredSlotId = eSlots[11].Id };
        var prefersGoeppingenP001 = new User { Id = Guid.NewGuid(), Email = $"g-{Guid.NewGuid():N}@x.de", DisplayName = "G", PreferredSlotId = gSlots[0].Id };
        db.Users.AddRange(admin, prefersErfurtP012, prefersGoeppingenP001);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bookingOnG5 = new Booking
        {
            Id = Guid.NewGuid(), UserId = admin.Id, LocationId = goeppingen.Id, ParkingSlotId = gSlots[4].Id,
            Date = today.AddDays(3), TimeSlot = TimeSlot.Morning, Status = BookingStatus.Won
        };
        var pastOnE12 = new Booking
        {
            Id = Guid.NewGuid(), UserId = admin.Id, LocationId = erfurt.Id, ParkingSlotId = eSlots[11].Id,
            Date = new DateOnly(2025, 3, 3), TimeSlot = TimeSlot.Morning, Status = BookingStatus.Confirmed
        };
        var futureOnE13 = new Booking
        {
            Id = Guid.NewGuid(), UserId = prefersErfurtP012.Id, LocationId = erfurt.Id, ParkingSlotId = eSlots[12].Id,
            Date = today.AddDays(5), TimeSlot = TimeSlot.Afternoon, Status = BookingStatus.Confirmed
        };
        db.Bookings.AddRange(bookingOnG5, pastOnE12, futureOnE13);

        db.BlockedDays.Add(new BlockedDay
        {
            Id = Guid.NewGuid(), LocationId = erfurt.Id, ParkingSlotId = eSlots[10].Id,
            Date = today.AddDays(2), Reason = "maintenance", BlockedByUserId = admin.Id, CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return new Scenario(goeppingen.Id, erfurt.Id, hessdorf.Id, other.Id,
            gSlots[4].Id, eSlots[10].Id, eSlots[11].Id, eSlots[12].Id, erfurtFree.Id,
            bookingOnG5.Id, prefersErfurtP012.Id, prefersGoeppingenP001.Id);
    }

    private async Task CleanupAsync(Scenario s)
    {
        await using var db = _fx.NewContext();
        await db.Users.Where(u => u.Id == s.UserPreferringErfurtP012 || u.Id == s.UserPreferringGoeppingenP001)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.PreferredSlotId, (Guid?)null));
        await db.Bookings.Where(b => b.LocationId == s.Goeppingen || b.LocationId == s.Erfurt).ExecuteDeleteAsync();
        await db.BlockedDays.Where(b => b.LocationId == s.Erfurt).ExecuteDeleteAsync();
        await db.Locations
            .Where(l => l.Id == s.Goeppingen || l.Id == s.Erfurt || l.Id == s.Hessdorf || l.Id == s.Other)
            .ExecuteDeleteAsync();   // slots and grid cells cascade
    }

    [SkippableFact]
    public async Task Up_RenumbersGoeppingenInPlace_AddsMissingSlots_TrimsSurplus_Idempotent()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var s = await SeedOldStateAsync();
        try
        {
            await using (var db = _fx.NewContext())
                await db.Database.ExecuteSqlRawAsync(RenumberSeededParkingSlots.UpSql);
            await AssertMigratedAsync(s);

            // Running the same statements again changes nothing.
            await using (var db = _fx.NewContext())
                await db.Database.ExecuteSqlRawAsync(RenumberSeededParkingSlots.UpSql);
            await AssertMigratedAsync(s);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    private async Task AssertMigratedAsync(Scenario s)
    {
        await using var db = _fx.NewContext();

        // --- Goeppingen: 28 slots, old ids kept, positions kept, new ones placed on free cells.
        var g = await db.ParkingSlots.Where(p => p.LocationId == s.Goeppingen).OrderBy(p => p.SlotNumber).ToListAsync();
        var expected = Enumerable.Range(97, 14).Concat(Enumerable.Range(122, 14)).Select(i => $"P{i:D3}").ToList();
        Assert.Equal(expected, g.Select(p => p.SlotNumber));
        Assert.All(g, p => Assert.True(p.IsActive));

        var p101 = g.Single(p => p.Id == s.GoeppingenP005);           // was P005 at (1,7)
        Assert.Equal("P101", p101.SlotNumber);
        Assert.Equal("Parking Spot 101", p101.Label);
        Assert.Equal((1, 7), (p101.GridRow, p101.GridColumn));
        Assert.Equal(s.GoeppingenP005, (await db.Bookings.FindAsync(s.BookingOnGoeppingenP005))!.ParkingSlotId);

        var p127 = g.Single(p => p.SlotNumber == "P127");             // was P020 at (4,8)
        Assert.Equal((4, 8), (p127.GridRow, p127.GridColumn));
        Assert.Equal((4, 10), (g.Single(p => p.SlotNumber == "P128").GridRow, g.Single(p => p.SlotNumber == "P128").GridColumn));
        Assert.Equal((5, 1), (g.Single(p => p.SlotNumber == "P129").GridRow, g.Single(p => p.SlotNumber == "P129").GridColumn));
        Assert.Equal((5, 10), (g.Single(p => p.SlotNumber == "P135").GridRow, g.Single(p => p.SlotNumber == "P135").GridColumn));
        Assert.Equal("Parking Spot 135", g.Single(p => p.SlotNumber == "P135").Label);
        Assert.Equal(28, g.Select(p => (p.GridRow, p.GridColumn)).Distinct().Count());
        Assert.Equal(3, await db.GridCells.CountAsync(c => c.LocationId == s.Goeppingen && c.Row == 5));

        // The preference followed the slot id (P001 -> P097).
        var p097 = g.Single(p => p.SlotNumber == "P097");
        Assert.Equal(p097.Id, (await db.Users.FindAsync(s.UserPreferringGoeppingenP001))!.PreferredSlotId);

        // --- Erfurt: P001..P010 untouched, surplus handled per booking history.
        var e = await db.ParkingSlots.Where(p => p.LocationId == s.Erfurt).ToListAsync();
        Assert.Equal(10, e.Count(p => p.SlotNumber.CompareTo("P011") < 0 && p.SlotNumber.StartsWith("P")));
        Assert.All(e.Where(p => p.SlotNumber.CompareTo("P011") < 0 && p.SlotNumber.StartsWith("P")), p => Assert.True(p.IsActive && p.GridRow == 1));

        Assert.DoesNotContain(e, p => p.Id == s.ErfurtP011);                   // never booked: deleted
        Assert.Empty(await db.BlockedDays.Where(b => b.LocationId == s.Erfurt).ToListAsync());   // its block went with it
        Assert.DoesNotContain(e, p => p.SlotNumber == "P014" || p.SlotNumber == "P015");

        var e12 = e.Single(p => p.Id == s.ErfurtP012);                         // past booking only: deactivated
        Assert.False(e12.IsActive);
        Assert.Null(e12.GridRow);
        Assert.Null(e12.GridColumn);
        Assert.Equal("P012", e12.SlotNumber);
        Assert.Null((await db.Users.FindAsync(s.UserPreferringErfurtP012))!.PreferredSlotId);

        var e13 = e.Single(p => p.Id == s.ErfurtP013);                         // live future booking: untouched
        Assert.True(e13.IsActive);
        Assert.Equal((1, 12), (e13.GridRow, e13.GridColumn));

        Assert.Contains(e, p => p.Id == s.ErfurtFree && p.IsActive);           // non-seed number: untouched

        // --- Hessdorf: trimmed to 6. Other location: not a seed name, untouched.
        Assert.Equal(6, await db.ParkingSlots.CountAsync(p => p.LocationId == s.Hessdorf));
        Assert.Equal(30, await db.ParkingSlots.CountAsync(p => p.LocationId == s.Other));
    }

    [SkippableFact]
    public async Task Down_RestoresOldGoeppingenNumbers_AndDropsUnusedAddedSlots()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var s = await SeedOldStateAsync();
        try
        {
            await using (var db = _fx.NewContext())
            {
                await db.Database.ExecuteSqlRawAsync(RenumberSeededParkingSlots.UpSql);
                await db.Database.ExecuteSqlRawAsync(RenumberSeededParkingSlots.DownSql);
            }

            await using var check = _fx.NewContext();
            var g = await check.ParkingSlots.Where(p => p.LocationId == s.Goeppingen).OrderBy(p => p.SlotNumber).ToListAsync();
            Assert.Equal(Enumerable.Range(1, 20).Select(i => $"P{i:D3}"), g.Select(p => p.SlotNumber));
            var p005 = g.Single(p => p.Id == s.GoeppingenP005);
            Assert.Equal("Parking Spot 5", p005.Label);
            Assert.Equal((1, 7), (p005.GridRow, p005.GridColumn));
        }
        finally
        {
            await CleanupAsync(s);
        }
    }
}
