namespace SchulerPark.Infrastructure.Data.Seed;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

public static class SeedData
{
    private static readonly Guid AdminUserId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid TestUser1Id = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid TestUser2Id = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid TestUser3Id = Guid.Parse("a0000000-0000-0000-0000-000000000004");
    private static readonly Guid GoeppingenId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid ErfurtId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid HessdorfId = Guid.Parse("b0000000-0000-0000-0000-000000000003");
    private static readonly Guid GemmingenId = Guid.Parse("b0000000-0000-0000-0000-000000000004");
    private static readonly Guid WeingartenId = Guid.Parse("b0000000-0000-0000-0000-000000000005");
    private static readonly Guid NetphenId = Guid.Parse("b0000000-0000-0000-0000-000000000006");

    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        await SeedUsers(context, passwordHasher);
        await SeedLocations(context);
        await SeedParkingSlots(context);
        await SeedUserPreferredSlots(context);
        await SeedTestBookings(context);
        await SeedBlockedDays(context);

        await context.SaveChangesAsync();

        await SeedGridLayouts(context);
    }

    private static async Task SeedUsers(AppDbContext context, IPasswordHasher<User> passwordHasher)
    {
        var baseUsers = new[]
        {
            new User
            {
                Id = AdminUserId,
                Email = "admin@schulerpark.local",
                DisplayName = "System Administrator",
                Role = UserRole.Admin,
                CarLicensePlate = "GP-AD 1000",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = TestUser1Id,
                Email = "anna.mueller@schuler.de",
                DisplayName = "Anna Mueller",
                Role = UserRole.User,
                CarLicensePlate = "GP-AM 2001",
                PreferredLocationId = GoeppingenId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = TestUser2Id,
                Email = "max.schmidt@schuler.de",
                DisplayName = "Max Schmidt",
                Role = UserRole.User,
                CarLicensePlate = "EF-MS 3002",
                PreferredLocationId = ErfurtId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = TestUser3Id,
                Email = "lisa.weber@schuler.de",
                DisplayName = "Lisa Weber",
                Role = UserRole.User,
                CarLicensePlate = "HD-LW 4003",
                PreferredLocationId = WeingartenId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        // 16 additional seed users (total = 20). Deterministic IDs so re-seeding is idempotent.
        var extras = new (string IdHex, string Email, string Display, string? Plate, Guid? PrefLoc)[]
        {
            ("05", "felix.bauer@schuler.de",      "Felix Bauer",       "GP-FB 5001", GoeppingenId),
            ("06", "sophie.fischer@schuler.de",   "Sophie Fischer",    "GP-SF 5002", GoeppingenId),
            ("07", "jonas.hoffmann@schuler.de",   "Jonas Hoffmann",    "EF-JH 5003", ErfurtId),
            ("08", "marie.koch@schuler.de",       "Marie Koch",        "EF-MK 5004", ErfurtId),
            ("09", "leon.richter@schuler.de",     "Leon Richter",      "HD-LR 5005", HessdorfId),
            ("0a", "emma.wolf@schuler.de",        "Emma Wolf",         "HD-EW 5006", HessdorfId),
            ("0b", "paul.neumann@schuler.de",     "Paul Neumann",      "GM-PN 5007", GemmingenId),
            ("0c", "mia.schwarz@schuler.de",      "Mia Schwarz",       "GM-MS 5008", GemmingenId),
            ("0d", "noah.zimmermann@schuler.de",  "Noah Zimmermann",   "WG-NZ 5009", WeingartenId),
            ("0e", "lena.krueger@schuler.de",     "Lena Krüger",       "WG-LK 5010", WeingartenId),
            ("0f", "elias.hartmann@schuler.de",   "Elias Hartmann",    "SI-EH 5011", NetphenId),
            ("10", "clara.lange@schuler.de",      "Clara Lange",       "SI-CL 5012", NetphenId),
            ("11", "finn.werner@schuler.de",      "Finn Werner",       "GP-FW 5013", null),
            ("12", "julia.peters@schuler.de",     "Julia Peters",      "EF-JP 5014", null),
            ("13", "david.kaiser@schuler.de",     "David Kaiser",      null,          null),
            ("14", "sara.vogel@schuler.de",       "Sara Vogel",        null,          null),
        };

        var users = baseUsers.Concat(extras.Select(e => new User
        {
            Id = Guid.Parse($"a0000000-0000-0000-0000-0000000000{e.IdHex}"),
            Email = e.Email,
            DisplayName = e.Display,
            Role = UserRole.User,
            CarLicensePlate = e.Plate,
            PreferredLocationId = e.PrefLoc,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        })).ToArray();

        foreach (var user in users)
        {
            if (!await context.Users.AnyAsync(u => u.Id == user.Id))
            {
                var password = user.Id == AdminUserId ? "Admin123!" : "Test1234!";
                user.PasswordHash = passwordHasher.HashPassword(user, password);
                context.Users.Add(user);
            }
        }
    }

    private static async Task SeedLocations(AppDbContext context)
    {
        var locations = new[]
        {
            new Location
            {
                Id = GoeppingenId,
                Name = "Goeppingen",
                Address = "Schuler Pressen GmbH, Bahnhofstraße 41, 73033 Göppingen",
                DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
            },
            new Location
            {
                Id = ErfurtId,
                Name = "Erfurt",
                Address = "Schuler Pressen GmbH, Erfurt Office",
                DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
            },
            new Location
            {
                Id = HessdorfId,
                Name = "Hessdorf",
                Address = "Schuler Pressen GmbH, Hessdorf Office",
                DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
            },
            new Location
            {
                Id = GemmingenId,
                Name = "Gemmingen",
                Address = "Schuler Pressen GmbH, Gemmingen Office",
                DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
            },
            new Location
            {
                Id = WeingartenId,
                Name = "Weingarten",
                Address = "Schuler Pressen GmbH, Schillerstraße, 88250 Weingarten",
                DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
            },
            new Location
            {
                Id = NetphenId,
                Name = "Netphen",
                Address = "Schuler Pressen GmbH, Netphen Office",
                DefaultAlgorithm = LotteryAlgorithm.WeightedHistory
            }
        };

        foreach (var location in locations)
        {
            if (!await context.Locations.AnyAsync(l => l.Id == location.Id))
                context.Locations.Add(location);
        }
    }

    private static async Task SeedParkingSlots(AppDbContext context)
    {
        var slotCounts = new Dictionary<Guid, int>
        {
            { GoeppingenId, 20 },
            { ErfurtId, 15 },
            { HessdorfId, 10 },
            { GemmingenId, 12 },
            { WeingartenId, 18 },
            { NetphenId, 8 }
        };

        foreach (var (locationId, count) in slotCounts)
        {
            if (await context.ParkingSlots.AnyAsync(ps => ps.LocationId == locationId))
                continue;

            for (var i = 1; i <= count; i++)
            {
                context.ParkingSlots.Add(new ParkingSlot
                {
                    Id = Guid.NewGuid(),
                    LocationId = locationId,
                    SlotNumber = $"P{i:D3}",
                    Label = $"Parking Spot {i}",
                    IsActive = true
                });
            }
        }
    }

    // Assign preferred parking slots to a subset of seed users so the feature
    // has meaningful demo data. Idempotent: only fills in when PreferredSlotId
    // is null, so operator-chosen preferences in prod are never overwritten.
    private static async Task SeedUserPreferredSlots(AppDbContext context)
    {
        var assignments = new (Guid UserId, Guid LocationId, string SlotNumber)[]
        {
            (TestUser1Id,                                            GoeppingenId, "P001"), // Anna
            (TestUser2Id,                                            ErfurtId,     "P001"), // Max
            (TestUser3Id,                                            WeingartenId, "P001"), // Lisa
            (Guid.Parse("a0000000-0000-0000-0000-000000000005"),     GoeppingenId, "P005"), // Felix
            (Guid.Parse("a0000000-0000-0000-0000-000000000006"),     GoeppingenId, "P006"), // Sophie
            (Guid.Parse("a0000000-0000-0000-0000-000000000007"),     ErfurtId,     "P005"), // Jonas
            (Guid.Parse("a0000000-0000-0000-0000-000000000009"),     HessdorfId,   "P001"), // Leon
            (Guid.Parse("a0000000-0000-0000-0000-00000000000b"),     GemmingenId,  "P001"), // Paul
            (Guid.Parse("a0000000-0000-0000-0000-00000000000d"),     WeingartenId, "P013"), // Noah
            (Guid.Parse("a0000000-0000-0000-0000-00000000000f"),     NetphenId,    "P001"), // Elias
        };

        foreach (var (userId, locationId, slotNumber) in assignments)
        {
            var user = context.Users.Local.FirstOrDefault(u => u.Id == userId)
                ?? await context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null || user.PreferredSlotId is not null)
                continue;

            var slot = context.ParkingSlots.Local
                .FirstOrDefault(s => s.LocationId == locationId && s.SlotNumber == slotNumber)
                ?? await context.ParkingSlots
                    .FirstOrDefaultAsync(s => s.LocationId == locationId && s.SlotNumber == slotNumber);
            if (slot is null)
                continue;

            user.PreferredSlotId = slot.Id;
        }
    }

    private static async Task SeedTestBookings(AppDbContext context)
    {
        var berlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, berlinTz);
        var today = DateOnly.FromDateTime(berlinNow);
        var tomorrow = today.AddDays(1);

        // Remove stale test bookings (dates in the past) and re-seed
        var staleBookings = await context.Bookings
            .Where(b => new[] { TestUser1Id, TestUser2Id, TestUser3Id }.Contains(b.UserId)
                        && b.Date < tomorrow)
            .ToListAsync();

        if (staleBookings.Any())
            context.Bookings.RemoveRange(staleBookings);

        // Skip if future test bookings already exist
        if (await context.Bookings.AnyAsync(b => b.UserId == TestUser1Id && b.Date >= tomorrow))
            return;
        var dayAfter = today.AddDays(2);
        var nextWeek = today.AddDays(7);

        // --- Bookings for tomorrow (ready for lottery) ---

        // Anna: Goeppingen Morning (Pending — ready for lottery)
        context.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = TestUser1Id,
            LocationId = GoeppingenId,
            Date = tomorrow,
            TimeSlot = TimeSlot.Morning,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // Max: Goeppingen Morning (Pending — same slot, tests lottery competition)
        context.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = TestUser2Id,
            LocationId = GoeppingenId,
            Date = tomorrow,
            TimeSlot = TimeSlot.Morning,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // Lisa: Goeppingen Afternoon (Pending)
        context.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = TestUser3Id,
            LocationId = GoeppingenId,
            Date = tomorrow,
            TimeSlot = TimeSlot.Afternoon,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // Anna: Erfurt Morning (Pending — cross-location booking)
        context.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = TestUser1Id,
            LocationId = ErfurtId,
            Date = tomorrow,
            TimeSlot = TimeSlot.Morning,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // --- Bookings for day after tomorrow ---

        // All 3 users at Hessdorf Morning (tests lottery with 3 candidates)
        foreach (var userId in new[] { TestUser1Id, TestUser2Id, TestUser3Id })
        {
            context.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LocationId = HessdorfId,
                Date = dayAfter,
                TimeSlot = TimeSlot.Morning,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        // --- Bookings for next week ---

        // Max: Gemmingen Morning
        context.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = TestUser2Id,
            LocationId = GemmingenId,
            Date = nextWeek,
            TimeSlot = TimeSlot.Morning,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // Lisa: Gemmingen Afternoon
        context.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            UserId = TestUser3Id,
            LocationId = GemmingenId,
            Date = nextWeek,
            TimeSlot = TimeSlot.Afternoon,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task SeedBlockedDays(AppDbContext context)
    {
        var berlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, berlinTz);
        var today = DateOnly.FromDateTime(berlinNow);

        // Remove stale blocked days (dates in the past) and re-seed
        var staleBlocked = await context.BlockedDays
            .Where(b => b.Date < today)
            .ToListAsync();

        if (staleBlocked.Any())
            context.BlockedDays.RemoveRange(staleBlocked);

        // Skip if future blocked days already exist
        if (await context.BlockedDays.AnyAsync(b => b.Date >= today))
            return;

        // Block Goeppingen 3 days from now (location-wide, e.g., public holiday)
        context.BlockedDays.Add(new BlockedDay
        {
            Id = Guid.NewGuid(),
            LocationId = GoeppingenId,
            Date = today.AddDays(3),
            Reason = "Public Holiday — Tag der Arbeit",
            BlockedByUserId = AdminUserId,
            CreatedAt = DateTime.UtcNow
        });

        // Block specific slot at Erfurt 4 days from now (maintenance)
        var erfurtSlot = await context.ParkingSlots
            .FirstOrDefaultAsync(s => s.LocationId == ErfurtId && s.IsActive);
        if (erfurtSlot != null)
        {
            context.BlockedDays.Add(new BlockedDay
            {
                Id = Guid.NewGuid(),
                LocationId = ErfurtId,
                ParkingSlotId = erfurtSlot.Id,
                Date = today.AddDays(4),
                Reason = "Maintenance — Slot repair",
                BlockedByUserId = AdminUserId,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Seed grid layouts based on real Schuler parking lot floor plans.
    /// Göppingen: large lot with parking columns, central road, charging area.
    /// Erfurt: two parking zones (upper/lower) with building on right.
    /// Hessdorf: building in center, parking around edges.
    /// Gemmingen: buildings (Bau) on right, parking on left.
    /// </summary>
    private static async Task SeedGridLayouts(AppDbContext context)
    {
        // Skip if any location already has a grid configured
        if (await context.Locations.AnyAsync(l => l.GridRows != null))
            return;

        await SeedGoeppingenGrid(context);
        await SeedErfurtGrid(context);
        await SeedHessdorfGrid(context);
        await SeedGemmingenGrid(context);
        await SeedWeingartenGrid(context);
        await SeedNetphenGrid(context);

        await context.SaveChangesAsync();
    }

    // Göppingen: 8 rows × 12 columns
    // Based on the detailed parking lot plan — parking columns separated by driving
    // lanes, building along the top, entrance at bottom center.
    //
    //      0    1    2    3    4    5    6    7    8    9   10   11
    // 0: [OBS][OBS][OBS][OBS][OBS][OBS][OBS][OBS][OBS][OBS][OBS][OBS]  building
    // 1: [   ][P01][P02][ RD][P03][P04][ RD][P05][P06][ RD][P07][   ]
    // 2: [   ][P08][P09][ RD][P10][P11][ RD][P12][P13][ RD][P14][   ]
    // 3: [   ][   ][   ][ RD][   ][   ][ RD][   ][   ][ RD][   ][   ]  lane
    // 4: [   ][P15][P16][ RD][P17][P18][ RD][P19][P20][   ][   ][   ]
    // 5: [   ][   ][   ][ RD][   ][   ][ RD][   ][   ][   ][   ][   ]  lane
    // 6: [   ][   ][   ][ RD][ RD][ RD][ RD][   ][   ][   ][   ][   ]  exit road
    // 7: [   ][   ][   ][   ][   ][ENT][   ][   ][   ][   ][   ][   ]  entrance
    private static async Task SeedGoeppingenGrid(AppDbContext context)
    {
        var location = await context.Locations.FindAsync(GoeppingenId);
        if (location == null) return;

        location.GridRows = 8;
        location.GridColumns = 12;

        var slots = await context.ParkingSlots
            .Where(s => s.LocationId == GoeppingenId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        // Slot positions (row, col) for P001..P020
        var positions = new (int Row, int Col)[]
        {
            (1, 1), (1, 2), (1, 4), (1, 5), (1, 7), (1, 8), (1, 10),     // P001-P007
            (2, 1), (2, 2), (2, 4), (2, 5), (2, 7), (2, 8), (2, 10),     // P008-P014
            (4, 1), (4, 2), (4, 4), (4, 5), (4, 7), (4, 8),              // P015-P020
        };

        for (var i = 0; i < Math.Min(slots.Count, positions.Length); i++)
        {
            slots[i].GridRow = positions[i].Row;
            slots[i].GridColumn = positions[i].Col;
        }

        // Building (obstacle) along top row
        for (var c = 0; c < 12; c++)
            AddCell(context, GoeppingenId, 0, c, GridCellType.Obstacle);

        // Vertical roads (columns 3, 6, 9) rows 1-6
        foreach (var col in new[] { 3, 6, 9 })
        {
            for (var r = 1; r <= 5; r++)
                AddCell(context, GoeppingenId, r, col, GridCellType.Road);
        }

        // Horizontal exit road (row 6, cols 3-6)
        AddCell(context, GoeppingenId, 6, 3, GridCellType.Road);
        AddCell(context, GoeppingenId, 6, 4, GridCellType.Road);
        AddCell(context, GoeppingenId, 6, 5, GridCellType.Road);
        AddCell(context, GoeppingenId, 6, 6, GridCellType.Road);

        // Entrance
        AddCell(context, GoeppingenId, 7, 5, GridCellType.Entrance);

        // Labels
        AddCell(context, GoeppingenId, 3, 1, GridCellType.Label, "A");
        AddCell(context, GoeppingenId, 3, 5, GridCellType.Label, "B");
        AddCell(context, GoeppingenId, 3, 8, GridCellType.Label, "C");
    }

    // Erfurt: 8 rows × 10 columns
    // Based on aerial photo — two separate parking zones connected by a road,
    // building (obstacle) on the right side.
    //
    //      0    1    2    3    4    5    6    7    8    9
    // 0: [   ][   ][   ][   ][   ][   ][   ][OBS][OBS][OBS]
    // 1: [   ][P01][P02][P03][P04][ RD][P05][P06][   ][   ]  upper zone
    // 2: [   ][P07][P08][P09][   ][ RD][P10][   ][   ][   ]  upper zone
    // 3: [ RD][ RD][ RD][ RD][ RD][ RD][ RD][ RD][ RD][ RD]  main road
    // 4: [   ][   ][   ][   ][   ][   ][   ][OBS][OBS][OBS]
    // 5: [   ][P11][P12][P13][ RD][P14][P15][   ][   ][   ]  lower zone
    // 6: [   ][   ][   ][   ][ RD][   ][   ][   ][   ][   ]
    // 7: [   ][   ][   ][ENT][ RD][   ][   ][   ][   ][   ]  entrance
    private static async Task SeedErfurtGrid(AppDbContext context)
    {
        var location = await context.Locations.FindAsync(ErfurtId);
        if (location == null) return;

        location.GridRows = 8;
        location.GridColumns = 10;

        var slots = await context.ParkingSlots
            .Where(s => s.LocationId == ErfurtId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        var positions = new (int Row, int Col)[]
        {
            (1, 1), (1, 2), (1, 3), (1, 4), (1, 6), (1, 7),     // P001-P006
            (2, 1), (2, 2), (2, 3), (2, 6),                       // P007-P010
            (5, 1), (5, 2), (5, 3), (5, 5), (5, 6),               // P011-P015
        };

        for (var i = 0; i < Math.Min(slots.Count, positions.Length); i++)
        {
            slots[i].GridRow = positions[i].Row;
            slots[i].GridColumn = positions[i].Col;
        }

        // Building obstacle (top-right and mid-right)
        for (var c = 7; c <= 9; c++)
        {
            AddCell(context, ErfurtId, 0, c, GridCellType.Obstacle);
            AddCell(context, ErfurtId, 4, c, GridCellType.Obstacle);
        }

        // Horizontal main road (row 3, full width)
        for (var c = 0; c < 10; c++)
            AddCell(context, ErfurtId, 3, c, GridCellType.Road);

        // Vertical roads
        AddCell(context, ErfurtId, 1, 5, GridCellType.Road);
        AddCell(context, ErfurtId, 2, 5, GridCellType.Road);
        AddCell(context, ErfurtId, 5, 4, GridCellType.Road);
        AddCell(context, ErfurtId, 6, 4, GridCellType.Road);
        AddCell(context, ErfurtId, 7, 4, GridCellType.Road);

        // Entrance
        AddCell(context, ErfurtId, 7, 3, GridCellType.Entrance);

        // Zone labels
        AddCell(context, ErfurtId, 0, 3, GridCellType.Label, "Zone 1-2");
        AddCell(context, ErfurtId, 4, 3, GridCellType.Label, "Zone 3-4");
    }

    // Hessdorf: 6 rows × 8 columns
    // Based on satellite photo — building in center, parking around edges,
    // road along the bottom.
    //
    //      0    1    2    3    4    5    6    7
    // 0: [   ][P01][P02][   ][   ][P03][P04][   ]
    // 1: [   ][P05][   ][OBS][OBS][   ][P06][   ]
    // 2: [   ][   ][   ][OBS][OBS][   ][   ][   ]
    // 3: [   ][P07][   ][OBS][OBS][   ][P08][   ]
    // 4: [   ][P09][P10][   ][   ][   ][   ][   ]
    // 5: [ RD][ RD][ RD][ RD][ RD][ RD][ENT][   ]  road + entrance
    private static async Task SeedHessdorfGrid(AppDbContext context)
    {
        var location = await context.Locations.FindAsync(HessdorfId);
        if (location == null) return;

        location.GridRows = 6;
        location.GridColumns = 8;

        var slots = await context.ParkingSlots
            .Where(s => s.LocationId == HessdorfId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        var positions = new (int Row, int Col)[]
        {
            (0, 1), (0, 2), (0, 5), (0, 6),   // P001-P004
            (1, 1), (1, 6),                     // P005-P006
            (3, 1), (3, 6),                     // P007-P008
            (4, 1), (4, 2),                     // P009-P010
        };

        for (var i = 0; i < Math.Min(slots.Count, positions.Length); i++)
        {
            slots[i].GridRow = positions[i].Row;
            slots[i].GridColumn = positions[i].Col;
        }

        // Central building (obstacle 2×2 at rows 1-3, cols 3-4)
        for (var r = 1; r <= 3; r++)
            for (var c = 3; c <= 4; c++)
                AddCell(context, HessdorfId, r, c, GridCellType.Obstacle);

        // Road along bottom (row 5, cols 0-5)
        for (var c = 0; c <= 5; c++)
            AddCell(context, HessdorfId, 5, c, GridCellType.Road);

        // Entrance
        AddCell(context, HessdorfId, 5, 6, GridCellType.Entrance);
    }

    // Gemmingen: 7 rows × 9 columns
    // Based on site plan (Bau 1, 7, 8, 14) — buildings on the right side,
    // parking area on the left, vertical road through the middle.
    //
    //      0    1    2    3    4    5    6    7    8
    // 0: [LBL][   ][   ][   ][ RD][OBS][OBS][OBS][OBS]  Bau 14
    // 1: [P01][P02][P03][   ][ RD][OBS][OBS][OBS][OBS]
    // 2: [P04][P05][P06][   ][ RD][   ][   ][   ][   ]
    // 3: [   ][   ][   ][   ][ RD][   ][   ][   ][   ]  lane
    // 4: [P07][P08][P09][   ][ RD][OBS][OBS][OBS][OBS]  Bau 8
    // 5: [P10][P11][P12][   ][ RD][OBS][OBS][OBS][OBS]  Bau 7
    // 6: [   ][   ][ENT][ RD][ RD][   ][   ][   ][   ]  entrance
    private static async Task SeedGemmingenGrid(AppDbContext context)
    {
        var location = await context.Locations.FindAsync(GemmingenId);
        if (location == null) return;

        location.GridRows = 7;
        location.GridColumns = 9;

        var slots = await context.ParkingSlots
            .Where(s => s.LocationId == GemmingenId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        var positions = new (int Row, int Col)[]
        {
            (1, 0), (1, 1), (1, 2),   // P001-P003
            (2, 0), (2, 1), (2, 2),   // P004-P006
            (4, 0), (4, 1), (4, 2),   // P007-P009
            (5, 0), (5, 1), (5, 2),   // P010-P012
        };

        for (var i = 0; i < Math.Min(slots.Count, positions.Length); i++)
        {
            slots[i].GridRow = positions[i].Row;
            slots[i].GridColumn = positions[i].Col;
        }

        // Buildings on right side (Bau 14 top, Bau 8/7 bottom)
        for (var c = 5; c <= 8; c++)
        {
            AddCell(context, GemmingenId, 0, c, GridCellType.Obstacle);
            AddCell(context, GemmingenId, 1, c, GridCellType.Obstacle);
            AddCell(context, GemmingenId, 4, c, GridCellType.Obstacle);
            AddCell(context, GemmingenId, 5, c, GridCellType.Obstacle);
        }

        // Vertical road (column 4, rows 0-5)
        for (var r = 0; r <= 5; r++)
            AddCell(context, GemmingenId, r, 4, GridCellType.Road);

        // Horizontal exit road + entrance (row 6)
        AddCell(context, GemmingenId, 6, 3, GridCellType.Road);
        AddCell(context, GemmingenId, 6, 4, GridCellType.Road);

        // Entrance
        AddCell(context, GemmingenId, 6, 2, GridCellType.Entrance);

        // Labels
        AddCell(context, GemmingenId, 0, 0, GridCellType.Label, "West");
    }

    // Weingarten: 9 rows × 11 columns
    // Based on site plan (Bau 1, 7, 8, 14, 21) — Schussenstrasse at top,
    // Schillerstraße at bottom. Buildings in center/right, long row of
    // parking along the west (left) side, road running north-south.
    //
    //      0    1    2    3    4    5    6    7    8    9   10
    // 0: [   ][   ][   ][   ][ RD][OBS][OBS][LBL][   ][ RD][OBS]  Bau14     Bau21
    // 1: [P01][P02][P03][   ][ RD][OBS][OBS][OBS][   ][ RD][OBS]
    // 2: [P04][P05][P06][   ][ RD][   ][OBS][OBS][   ][ RD][   ]  Bau 1
    // 3: [P07][P08][P09][   ][ RD][   ][OBS][OBS][   ][ RD][   ]
    // 4: [P10][P11][P12][   ][ RD][   ][   ][   ][   ][ RD][   ]  lane
    // 5: [P13][P14][P15][   ][ RD][   ][OBS][OBS][OBS][ RD][   ]  Bau 8
    // 6: [P16][P17][P18][   ][ RD][   ][   ][OBS][OBS][ RD][   ]  Bau 7
    // 7: [   ][   ][   ][   ][ RD][ RD][ RD][ RD][ RD][ RD][   ]  road
    // 8: [   ][   ][ENT][   ][   ][   ][LBL][   ][   ][   ][   ]  Schillerstr
    private static async Task SeedWeingartenGrid(AppDbContext context)
    {
        var location = await context.Locations.FindAsync(WeingartenId);
        if (location == null) return;

        location.GridRows = 9;
        location.GridColumns = 11;

        var slots = await context.ParkingSlots
            .Where(s => s.LocationId == WeingartenId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        // 18 slots along the west (left) side in 6 rows of 3
        var positions = new (int Row, int Col)[]
        {
            (1, 0), (1, 1), (1, 2),   // P001-P003
            (2, 0), (2, 1), (2, 2),   // P004-P006
            (3, 0), (3, 1), (3, 2),   // P007-P009
            (4, 0), (4, 1), (4, 2),   // P010-P012
            (5, 0), (5, 1), (5, 2),   // P013-P015
            (6, 0), (6, 1), (6, 2),   // P016-P018
        };

        for (var i = 0; i < Math.Min(slots.Count, positions.Length); i++)
        {
            slots[i].GridRow = positions[i].Row;
            slots[i].GridColumn = positions[i].Col;
        }

        // Vertical road (column 4, rows 0-7) and (column 9, rows 0-7)
        for (var r = 0; r <= 7; r++)
        {
            AddCell(context, WeingartenId, r, 4, GridCellType.Road);
            AddCell(context, WeingartenId, r, 9, GridCellType.Road);
        }

        // Horizontal road (row 7, cols 5-8)
        for (var c = 5; c <= 8; c++)
            AddCell(context, WeingartenId, 7, c, GridCellType.Road);

        // Bau 14 (top-left building, rows 0-1, cols 5-6)
        AddCell(context, WeingartenId, 0, 5, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 0, 6, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 1, 5, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 1, 6, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 1, 7, GridCellType.Obstacle);

        // Bau 21 (top-right, rows 0-1, col 10)
        AddCell(context, WeingartenId, 0, 10, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 1, 10, GridCellType.Obstacle);

        // Bau 1 (center, rows 2-3, cols 6-7)
        AddCell(context, WeingartenId, 2, 6, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 2, 7, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 3, 6, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 3, 7, GridCellType.Obstacle);

        // Bau 8 (bottom-center, row 5, cols 6-8)
        AddCell(context, WeingartenId, 5, 6, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 5, 7, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 5, 8, GridCellType.Obstacle);

        // Bau 7 (bottom-right, row 6, cols 7-8)
        AddCell(context, WeingartenId, 6, 7, GridCellType.Obstacle);
        AddCell(context, WeingartenId, 6, 8, GridCellType.Obstacle);

        // Entrance
        AddCell(context, WeingartenId, 8, 2, GridCellType.Entrance);

        // Labels
        AddCell(context, WeingartenId, 0, 7, GridCellType.Label, "Bau 14");
        AddCell(context, WeingartenId, 8, 6, GridCellType.Label, "Schillerstr.");
    }

    // Netphen: 6 rows × 8 columns
    // Smaller office location — compact rectangular lot with building
    // on the right, two rows of parking on the left, road at bottom.
    //
    //      0    1    2    3    4    5    6    7
    // 0: [LBL][   ][   ][   ][   ][OBS][OBS][OBS]  office
    // 1: [P01][P02][P03][P04][ RD][OBS][OBS][OBS]
    // 2: [   ][   ][   ][   ][ RD][OBS][OBS][OBS]  lane
    // 3: [P05][P06][P07][P08][ RD][   ][   ][   ]
    // 4: [ RD][ RD][ RD][ RD][ RD][   ][   ][   ]  road
    // 5: [   ][ENT][   ][   ][   ][   ][   ][   ]  entrance
    private static async Task SeedNetphenGrid(AppDbContext context)
    {
        var location = await context.Locations.FindAsync(NetphenId);
        if (location == null) return;

        location.GridRows = 6;
        location.GridColumns = 8;

        var slots = await context.ParkingSlots
            .Where(s => s.LocationId == NetphenId)
            .OrderBy(s => s.SlotNumber)
            .ToListAsync();

        // 8 slots in 2 rows of 4
        var positions = new (int Row, int Col)[]
        {
            (1, 0), (1, 1), (1, 2), (1, 3),   // P001-P004
            (3, 0), (3, 1), (3, 2), (3, 3),   // P005-P008
        };

        for (var i = 0; i < Math.Min(slots.Count, positions.Length); i++)
        {
            slots[i].GridRow = positions[i].Row;
            slots[i].GridColumn = positions[i].Col;
        }

        // Office building (top-right, rows 0-2, cols 5-7)
        for (var r = 0; r <= 2; r++)
            for (var c = 5; c <= 7; c++)
                AddCell(context, NetphenId, r, c, GridCellType.Obstacle);

        // Vertical road (column 4, rows 1-3)
        for (var r = 1; r <= 3; r++)
            AddCell(context, NetphenId, r, 4, GridCellType.Road);

        // Horizontal road (row 4, cols 0-4)
        for (var c = 0; c <= 4; c++)
            AddCell(context, NetphenId, 4, c, GridCellType.Road);

        // Entrance
        AddCell(context, NetphenId, 5, 1, GridCellType.Entrance);

        // Label
        AddCell(context, NetphenId, 0, 0, GridCellType.Label, "Netphen");
    }

    private static void AddCell(AppDbContext context, Guid locationId,
        int row, int column, GridCellType cellType, string? label = null)
    {
        context.GridCells.Add(new GridCell
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Row = row,
            Column = column,
            CellType = cellType,
            Label = label
        });
    }
}
