namespace SchulerPark.Infrastructure.Data.Seed;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

public static class SeedData
{
    private static readonly Guid AdminUserId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid GoeppingenId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid ErfurtId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid HessdorfId = Guid.Parse("b0000000-0000-0000-0000-000000000003");
    private static readonly Guid GemmingenId = Guid.Parse("b0000000-0000-0000-0000-000000000004");

    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        await SeedAdminUser(context, passwordHasher);
        await SeedLocations(context);
        await SeedParkingSlots(context);

        await context.SaveChangesAsync();
    }

    private static async Task SeedAdminUser(AppDbContext context, IPasswordHasher<User> passwordHasher)
    {
        if (await context.Users.AnyAsync(u => u.Id == AdminUserId))
            return;

        var admin = new User
        {
            Id = AdminUserId,
            Email = "admin@schulerpark.local",
            DisplayName = "System Administrator",
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, "Admin123!");
        context.Users.Add(admin);
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
}
