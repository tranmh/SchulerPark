namespace SchulerPark.Tests.Integration;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Jobs;
using Xunit;

// Simulates a mid-batch crash by throwing when a command tries to DELETE the
// "poison" user's row (its children delete normally first). Used to prove
// DataRetentionJob deletes each user atomically (bug #8).
file sealed class FailOnUserRowDeleteInterceptor(Guid poison) : DbCommandInterceptor
{
    private void Guard(DbCommand command)
    {
        if (!command.CommandText.Contains("DELETE FROM \"Users\"")) return;
        foreach (DbParameter p in command.Parameters)
            if (p.Value is Guid g && g == poison)
                throw new InvalidOperationException("Injected fault: simulated crash deleting user row.");
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Guard(command); return base.NonQueryExecuting(command, eventData, result); }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    { Guard(command); return base.NonQueryExecutingAsync(command, eventData, result, ct); }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Guard(command); return base.ReaderExecuting(command, eventData, result); }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    { Guard(command); return base.ReaderExecutingAsync(command, eventData, result, ct); }
}

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class DataRetentionAtomicityTests
{
    private readonly PostgresFixture _fx;
    public DataRetentionAtomicityTests(PostgresFixture fx) => _fx = fx;

    // ---- Bug #8: a mid-batch failure must never leave a user with children deleted but row present ----
    [SkippableFact]
    public async Task Mid_batch_failure_leaves_no_partially_deleted_user()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var stale = DateTime.UtcNow.AddDays(-40);   // past the 30-day grace window
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();                 // the poison user — its row delete will fault

        await using (var seed = _fx.NewContext())
        {
            var location = new Location { Id = Guid.NewGuid(), Name = "R", Address = "A" };
            seed.Add(location);
            foreach (var uid in new[] { user1, user2 })
            {
                seed.Users.Add(new User
                {
                    Id = uid,
                    Email = $"{uid:N}@x.de",
                    DisplayName = "D",
                    DeletedAt = stale,
                });
                seed.Bookings.Add(new Booking
                {
                    Id = Guid.NewGuid(),
                    UserId = uid,
                    LocationId = location.Id,
                    Date = new DateOnly(2026, 1, 1),
                    TimeSlot = TimeSlot.Morning,
                    Status = BookingStatus.Confirmed,
                });
            }
            await seed.SaveChangesAsync();
        }

        // Run retention with the fault injected on user2's row deletion.
        await using var db = _fx.NewContext(new FailOnUserRowDeleteInterceptor(user2));
        var job = new DataRetentionJob(db, NullLogger<DataRetentionJob>.Instance);
        await Assert.ThrowsAnyAsync<Exception>(() => job.ExecuteAsync());

        // Invariant: for every user, the row and its bookings are BOTH gone or BOTH present.
        // RED (non-atomic): children are ExecuteDelete-committed, then the row removal rolls
        // back -> user row present with no bookings. GREEN: per-user transaction rolls back
        // the children too, so the faulted user is left fully intact.
        await using var check = _fx.NewContext();
        foreach (var uid in new[] { user1, user2 })
        {
            var userExists = await check.Users.AnyAsync(u => u.Id == uid);
            var bookingExists = await check.Bookings.AnyAsync(b => b.UserId == uid);
            Assert.Equal(userExists, bookingExists);
        }
    }
}
