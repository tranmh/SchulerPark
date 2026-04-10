namespace SchulerPark.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ParkingSlot> ParkingSlots => Set<ParkingSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<LotteryRun> LotteryRuns => Set<LotteryRun>();
    public DbSet<LotteryHistory> LotteryHistories => Set<LotteryHistory>();
    public DbSet<BlockedDay> BlockedDays => Set<BlockedDay>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<GridCell> GridCells => Set<GridCell>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
