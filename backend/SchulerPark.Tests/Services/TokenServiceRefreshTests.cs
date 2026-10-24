namespace SchulerPark.Tests.Services;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;

// Refresh-token validation: expiry, revocation, reuse detection and account state.
public class TokenServiceRefreshTests
{
    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TokenService NewService(AppDbContext db) => new(
        db,
        Options.Create(new JwtSettings { RefreshExpiryDays = 7 }));

    private static User SeedUser(AppDbContext db, Action<User>? configure = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@schuler.de",
            DisplayName = "Token User",
            EmailVerified = true
        };
        configure?.Invoke(user);
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_FreshToken_ReturnsUser()
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var service = NewService(db);
        var raw = await service.GenerateRefreshTokenAsync(user, "127.0.0.1");

        var result = await service.ValidateRefreshTokenAsync(raw);

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_StoresHashNotRawToken()
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var service = NewService(db);

        var raw = await service.GenerateRefreshTokenAsync(user, "10.0.0.1");

        var row = await db.RefreshTokens.SingleAsync(rt => rt.UserId == user.Id);
        row.Token.Should().NotBe(raw);
        row.Token.Should().MatchRegex("^[0-9a-f]{64}$");
        row.CreatedByIp.Should().Be("10.0.0.1");
        row.RevokedAt.Should().BeNull();
        row.ExpiresAt.Should().BeAfter(row.CreatedAt.AddDays(6));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task ValidateRefreshTokenAsync_UnknownToken_ReturnsNull(string token)
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var service = NewService(db);
        await service.GenerateRefreshTokenAsync(user, null);

        (await service.ValidateRefreshTokenAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_ExpiredToken_ReturnsNullWithoutRevoking()
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var service = NewService(db);
        var raw = await service.GenerateRefreshTokenAsync(user, null);
        var row = await db.RefreshTokens.SingleAsync(rt => rt.UserId == user.Id);
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var result = await service.ValidateRefreshTokenAsync(raw);

        result.Should().BeNull();
        (await db.RefreshTokens.SingleAsync(rt => rt.UserId == user.Id)).RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_RevokedToken_ReturnsNullAndRevokesAllUserTokens()
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var other = SeedUser(db);
        var service = NewService(db);
        var stolen = await service.GenerateRefreshTokenAsync(user, null);
        var sibling = await service.GenerateRefreshTokenAsync(user, null);
        var otherUsers = await service.GenerateRefreshTokenAsync(other, null);
        await service.RevokeRefreshTokenAsync(stolen);

        var result = await service.ValidateRefreshTokenAsync(stolen);

        result.Should().BeNull();
        (await db.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync())
            .Should().OnlyContain(rt => rt.RevokedAt != null);
        (await service.ValidateRefreshTokenAsync(sibling)).Should().BeNull();
        (await service.ValidateRefreshTokenAsync(otherUsers)).Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_DeletedUser_ReturnsNull()
    {
        await using var db = NewDb();
        var user = SeedUser(db, u => u.DeletedAt = DateTime.UtcNow.AddDays(-1));
        var service = NewService(db);
        var raw = await service.GenerateRefreshTokenAsync(user, null);

        (await service.ValidateRefreshTokenAsync(raw)).Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_UnverifiedUser_ReturnsNull()
    {
        await using var db = NewDb();
        var user = SeedUser(db, u => u.EmailVerified = false);
        var service = NewService(db);
        var raw = await service.GenerateRefreshTokenAsync(user, null);

        (await service.ValidateRefreshTokenAsync(raw)).Should().BeNull();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_AlreadyRevoked_KeepsOriginalTimestamp()
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var service = NewService(db);
        var raw = await service.GenerateRefreshTokenAsync(user, null);
        var row = await db.RefreshTokens.SingleAsync(rt => rt.UserId == user.Id);
        var original = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        row.RevokedAt = original;
        await db.SaveChangesAsync();

        await service.RevokeRefreshTokenAsync(raw);

        (await db.RefreshTokens.SingleAsync(rt => rt.UserId == user.Id)).RevokedAt.Should().Be(original);
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_UnknownToken_DoesNotThrow()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var act = () => service.RevokeRefreshTokenAsync("does-not-exist");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAllUserTokensAsync_RevokesOnlyActiveTokensOfThatUser()
    {
        await using var db = NewDb();
        var user = SeedUser(db);
        var other = SeedUser(db);
        var service = NewService(db);
        var a = await service.GenerateRefreshTokenAsync(user, null);
        await service.GenerateRefreshTokenAsync(user, null);
        var keep = await service.GenerateRefreshTokenAsync(other, null);
        var earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldRow = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "already-revoked",
            CreatedAt = earlier,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = earlier
        };
        db.RefreshTokens.Add(oldRow);
        await db.SaveChangesAsync();

        await service.RevokeAllUserTokensAsync(user.Id);

        var rows = await db.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        rows.Should().HaveCount(3).And.OnlyContain(rt => rt.RevokedAt != null);
        rows.Single(rt => rt.Id == oldRow.Id).RevokedAt.Should().Be(earlier);
        (await service.ValidateRefreshTokenAsync(keep)).Should().NotBeNull();
        (await db.RefreshTokens.SingleAsync(rt => rt.UserId == other.Id)).RevokedAt.Should().BeNull();
        (await service.ValidateRefreshTokenAsync(a)).Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllUserTokensAsync_UserWithoutTokens_DoesNotThrow()
    {
        await using var db = NewDb();
        var service = NewService(db);

        var act = () => service.RevokeAllUserTokensAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }
}
