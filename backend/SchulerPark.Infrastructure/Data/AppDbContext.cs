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
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Bug #2: optimistic concurrency on Booking via PostgreSQL's system xmin column,
        // so two racing updates of the same booking can't silently clobber each other.
        // (Npgsql 10 removed UseXminAsConcurrencyToken; map the system column manually.)
        // Gated to Npgsql — the InMemory provider used in unit tests has no xmin.
        if (Database.IsNpgsql())
            modelBuilder.Entity<Booking>()
                .Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsRowVersion();
    }
}
