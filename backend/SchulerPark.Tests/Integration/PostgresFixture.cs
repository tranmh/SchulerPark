namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SchulerPark.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Shared real-PostgreSQL fixture for DB-integrity tests (unique/filtered indexes,
/// FK Restrict, transactions) that the EF InMemory provider cannot enforce.
///
/// Requires Docker. When Docker is unavailable the container fails to start and
/// <see cref="DockerAvailable"/> stays false — tests call
/// <c>Skip.IfNot(fx.DockerAvailable, fx.SkipReason)</c> and are reported Skipped,
/// never Failed. See docs/testing-integration-tests.md.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;

    public bool DockerAvailable { get; private set; }
    public string SkipReason { get; private set; } = "Docker unavailable";

    public async Task InitializeAsync()
    {
        try
        {
            // Build() itself probes the Docker endpoint, so it must be inside the try:
            // when Docker is down this is where it throws.
            _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _pg.StartAsync();
            await using var db = NewContext();
            await db.Database.MigrateAsync();   // apply the real schema (indexes, FK rules)
            DockerAvailable = true;
        }
        catch (Exception ex)
        {
            // Docker not installed / not running / image pull blocked — degrade to skip.
            DockerAvailable = false;
            SkipReason = $"Docker unavailable: {ex.GetType().Name}: {ex.Message}";
            if (_pg is not null)
            {
                try { await _pg.DisposeAsync(); } catch { /* best effort */ }
                _pg = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    /// <summary>Raw connection string to the container DB (for opening a separate connection).</summary>
    public string ConnectionString => _pg!.GetConnectionString();

    /// <summary>A fresh context on the container DB. Only valid when DockerAvailable.</summary>
    /// <param name="interceptors">Optional EF interceptors, e.g. to inject faults in tests.</param>
    public AppDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg!.GetConnectionString())
            .AddInterceptors(interceptors)
            .Options);
}

/// <summary>Tag DB-integrity test classes with <c>[Collection("Postgres")]</c> to share one container.</summary>
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
