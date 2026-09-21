namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;
using Xunit;

/// <summary>
/// Real-PostgreSQL companion to <see cref="PushTests"/>: the 410 → ExecuteDelete
/// pruning path cannot run on the EF InMemory provider, so the "row is actually
/// gone" assertion lives here.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class PushPruningDbTests
{
    private readonly PostgresFixture _fx;
    public PushPruningDbTests(PostgresFixture fx) => _fx = fx;

    private static PushSubscription Sub(Guid userId, string endpoint) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Endpoint = endpoint,
        P256dh = "p256dh",
        Auth = "auth",
        CreatedAt = DateTime.UtcNow
    };

    [SkippableFact]
    public async Task Expired_subscription_is_deleted_and_healthy_one_survives()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var userId = Guid.NewGuid();
        var healthy = $"https://push.example.test/{Guid.NewGuid():N}";
        var stale = $"https://push.example.test/{Guid.NewGuid():N}";

        await using (var seed = _fx.NewContext())
        {
            seed.Users.Add(new User { Id = userId, Email = $"u-{userId:N}@x.de", DisplayName = "U" });
            seed.PushSubscriptions.Add(Sub(userId, healthy));
            seed.PushSubscriptions.Add(Sub(userId, stale));
            await seed.SaveChangesAsync();
        }

        var sender = new RecordingPushSender();
        sender.GoneEndpoints[stale] = 1;

        await using (var db = _fx.NewContext())
        {
            var service = new PushNotificationService(
                db,
                sender,
                Options.Create(new VapidSettings { PublicKey = "pub", PrivateKey = "priv" }),
                NullLogger<PushNotificationService>.Instance);

            var result = await service.SendTestAsync(userId);

            Assert.Equal(2, result.Subscriptions);
            Assert.Equal(1, result.Delivered);
            Assert.Equal(1, result.Removed);
            Assert.Equal(0, result.Failed);
        }

        Assert.Contains(sender.Sent, s => s.Endpoint == healthy);

        await using var check = _fx.NewContext();
        Assert.False(await check.PushSubscriptions.AnyAsync(p => p.Endpoint == stale));
        Assert.True(await check.PushSubscriptions.AnyAsync(p => p.Endpoint == healthy));
    }
}
