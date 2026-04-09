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

    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        await SeedUsers(context, passwordHasher);
        await SeedLocations(context);
        await SeedParkingSlots(context);
        await SeedTestBookings(context);
        await SeedBlockedDays(context);

        await context.SaveChangesAsync();
    }

    private static async Task SeedUsers(AppDbContext context, IPasswordHasher<User> passwordHasher)
    {
        var users = new[]
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

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
            { GemmingenId, 12 }
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
}
