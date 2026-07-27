namespace SchulerPark.Tests.Integration;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Jobs;
using Xunit;

// Records every command text so a test can assert which statements the job issued.
file sealed class RecordingInterceptor : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    private void Rec(DbCommand c) => Commands.Add(c.CommandText);

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Rec(command); return base.NonQueryExecuting(command, eventData, result); }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    { Rec(command); return base.NonQueryExecutingAsync(command, eventData, result, ct); }
}

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class DataRetentionTests
{
    private readonly PostgresFixture _fx;
    public DataRetentionTests(PostgresFixture fx) => _fx = fx;

    // ---- Bug #17: prune bookings by Date (not CreatedAt) ----
    [SkippableFact]
    public async Task Old_booking_is_pruned_by_date_even_if_created_recently()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var bookingId = Guid.NewGuid();
        await using (var seed = _fx.NewContext())
        {
            var loc = new Location { Id = Guid.NewGuid(), Name = "R17", Address = "A" };
            var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@x.de", DisplayName = "U" };
            seed.AddRange(loc, user);
            seed.Bookings.Add(new Booking
            {
                Id = bookingId,
                UserId = user.Id,
                LocationId = loc.Id,
                Date = new DateOnly(2024, 1, 1),   // > 1 year ago
                TimeSlot = TimeSlot.Morning,
                Status = BookingStatus.Lost,
                // CreatedAt defaults to now() -> recent, as in an admin backfill
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.NewContext();
        await new DataRetentionJob(db, NullLogger<DataRetentionJob>.Instance).ExecuteAsync();

        // RED (prune on CreatedAt): survives because CreatedAt is recent.
        // GREEN (prune on Date): deleted because the booking day is > 1 year old.
        await using var check = _fx.NewContext();
        Assert.False(await check.Bookings.AnyAsync(b => b.Id == bookingId));
    }

    // ---- Bug #12: hard-delete explicitly erases push subscriptions ----
    [SkippableFact]
    public async Task Hard_delete_explicitly_erases_push_subscriptions()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var userId = Guid.NewGuid();
        await using (var seed = _fx.NewContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                Email = $"{Guid.NewGuid():N}@x.de",
                DisplayName = "U",
                DeletedAt = DateTime.UtcNow.AddDays(-40),   // past the grace window
            });
            seed.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = "https://push.example/endpoint",
                P256dh = "key",
                Auth = "auth",
            });
            await seed.SaveChangesAsync();
        }

        var recorder = new RecordingInterceptor();
        await using var db = _fx.NewContext(recorder);
        await new DataRetentionJob(db, NullLogger<DataRetentionJob>.Instance).ExecuteAsync();

        // RED: without the explicit purge, the row is only removed by the FK cascade — the job
        // issues no PushSubscriptions delete. GREEN: erasure is explicit in the deletion code.
        Assert.Contains(recorder.Commands, c => c.Contains("DELETE FROM \"PushSubscriptions\""));

        await using var check = _fx.NewContext();
        Assert.False(await check.PushSubscriptions.AnyAsync(p => p.UserId == userId));
    }
}
