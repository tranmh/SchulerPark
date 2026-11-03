using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// The real app (in-process TestServer) on the run's real PostgreSQL instead of EF InMemory:
/// the <see cref="CustomWebApplicationFactory"/> fakes (mail, push, Azure AD, clock) stay,
/// only the DbContext is swapped back to Npgsql.
/// </summary>
public class PgWebApplicationFactory : CustomWebApplicationFactory
{
    private readonly string _connectionString;

    public PgWebApplicationFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_connectionString));
        });
    }
}

/// <summary>
/// Creates a throw-away database on the run's PostgreSQL (AQAA_PG_CONNECTION_STRING or
/// AQAA_PG_URL), applies the real migrations and hosts the API on it. When neither variable is
/// set, <see cref="Available"/> stays false and tests skip via <c>Skip.IfNot</c>.
/// </summary>
public sealed class PgApiFixture : IAsyncLifetime
{
    private string? _serverConnectionString;
    private string? _databaseName;

    public bool Available { get; private set; }
    public string SkipReason { get; private set; } = "AQAA_PG_CONNECTION_STRING / AQAA_PG_URL not set";
    public PgWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var baseConnectionString = ResolveConnectionString();
        if (baseConnectionString == null)
            return;

        _serverConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Pooling = false }.ConnectionString;
        _databaseName = $"aqaa_it_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{_databaseName}\"");

        var testConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = _databaseName }.ConnectionString;
        Factory = new PgWebApplicationFactory(testConnectionString);

        // Program skips migrations in the Testing environment — apply the real schema here.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (Factory != null)
            await Factory.DisposeAsync();
        if (_databaseName != null && _serverConnectionString != null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        }
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_serverConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Npgsql key/value string from AQAA_PG_CONNECTION_STRING, else parsed from AQAA_PG_URL.</summary>
    public static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("AQAA_PG_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(cs))
            return cs;

        var url = Environment.GetEnvironmentVariable("AQAA_PG_URL");
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
        };
        if (userInfo.Length > 1)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        return builder.ConnectionString;
    }
}

/// <summary>Tag API-on-Postgres test classes with <c>[Collection("PgApi")]</c> to share one app + database.</summary>
[CollectionDefinition("PgApi")]
public class PgApiCollection : ICollectionFixture<PgApiFixture> { }
