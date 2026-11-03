using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SchulerPark.Core.Enums;

namespace SchulerPark.Tests.Integration;

/// <summary>/api/health against the real PostgreSQL schema (lottery-status queries included).</summary>
[Collection("PgApi")]
[Trait("Category", "Integration")]
public class PgHealthTests
{
    private readonly PgApiFixture _fx;

    public PgHealthTests(PgApiFixture fx) => _fx = fx;

    // 2031-03-11 is CET (UTC+1): 21:00 UTC = 22:00 Berlin, after the 21:30 report threshold.
    private static readonly DateTimeOffset AfterLotteryReport = new(2031, 3, 11, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeLotteryReport = new(2031, 3, 11, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Tomorrow = new(2031, 3, 12);

    private async Task<JsonElement> HealthAsync()
    {
        var response = await _fx.Factory.CreateClient().GetAsync("/api/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [SkippableFact]
    public async Task Health_WithDatabase_ReturnsHealthy()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var json = await HealthAsync();

        json.GetProperty("status").GetString().Should().Be("healthy");
        json.GetProperty("timestamp").GetDateTime().Should().NotBe(default);
    }

    [SkippableFact]
    public async Task Health_BeforeLotteryReportTime_LotteryIsNull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        using var _ = _fx.Factory.Clock.Pin(BeforeLotteryReport);

        var json = await HealthAsync();

        json.GetProperty("lottery").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task Health_AfterLotteryTime_ReportsMissingRunUntilTomorrowIsDrawn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var factory = _fx.Factory;
        var client = factory.CreateClient();
        var user = await AdminTestHelper.CreateUserWithRoleAsync(factory, client, UserRole.User, "health");
        var (locationId, _) = await AdminTestHelper.SeedLocationAsync(factory, 1);

        using var _ = factory.Clock.Pin(AfterLotteryReport);

        // No demand for tomorrow yet → nothing missing.
        var idle = await HealthAsync();
        idle.GetProperty("lottery").GetProperty("missingForTomorrow").GetBoolean().Should().BeFalse();

        // Pending demand for tomorrow without a lottery run → missing.
        await AdminTestHelper.SeedBookingAsync(factory, user.User.Id, locationId, null, Tomorrow, TimeSlot.Morning, BookingStatus.Pending);
        var missing = await HealthAsync();
        missing.GetProperty("lottery").GetProperty("missingForTomorrow").GetBoolean().Should().BeTrue();

        // The run for tomorrow exists → no longer missing, lastRunAt reported.
        await AdminTestHelper.SeedLotteryRunAsync(factory, locationId, Tomorrow, TimeSlot.Morning);
        var drawn = await HealthAsync();
        drawn.GetProperty("lottery").GetProperty("missingForTomorrow").GetBoolean().Should().BeFalse();
        drawn.GetProperty("lottery").GetProperty("lastRunAt").ValueKind.Should().Be(JsonValueKind.String);
    }
}

/// <summary>
/// Program startup guards outside Development/Testing: the host must refuse to start with a
/// weak Jwt:Secret or an unsafe connection string. No database is touched — the guards throw
/// before anything connects.
/// </summary>
[Trait("Category", "Integration")]
public class StartupValidationTests
{
    private const string SafeConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=S3cure-Pw_9x";
    private const string StrongSecret = "0123456789abcdef0123456789abcdef-strong";

    private sealed class StagingFactory(IReadOnlyDictionary<string, string> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        }
    }

    private static IEnumerable<string> Messages(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
        {
            yield return ex.Message;
            if (ex is AggregateException agg)
                foreach (var inner in agg.InnerExceptions)
                    foreach (var m in Messages(inner))
                        yield return m;
        }
    }

    private static Exception? StartWith(string connectionString, string jwtSecret)
    {
        using var factory = new StagingFactory(new Dictionary<string, string>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Jwt:Secret"] = jwtSecret
        });
        return Record.Exception(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_KEY_AT_LEAST_32_CHARS")]
    [InlineData("change_this-but-it-is-long-enough-to-pass-length")]
    [InlineData("too-short-secret")]
    [InlineData("0123456789abcdef0123456789abcde")] // 31 bytes
    public void Startup_WeakJwtSecretOutsideDevelopment_Throws(string secret)
    {
        var ex = StartWith(SafeConnectionString, secret);

        ex.Should().NotBeNull();
        Messages(ex).Should().Contain(m => m.Contains("Jwt:Secret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Host=db;Port=5432;Database=louise;Username=louise;Password=changeme")]
    public void Startup_UnsafeConnectionStringOutsideDevelopment_Throws(string connectionString)
    {
        var ex = StartWith(connectionString, StrongSecret);

        ex.Should().NotBeNull();
        Messages(ex).Should().Contain(m => m.Contains("ConnectionStrings:Default"));
    }

    [Fact]
    public void Startup_StrongSecretAndSafeConnectionString_PassesGuards()
    {
        // Startup gets past both guards; it then fails later only because the DB is unreachable.
        var ex = StartWith(SafeConnectionString, StrongSecret);

        Messages(ex).Should().NotContain(m => m.Contains("Jwt:Secret") || m.Contains("ConnectionStrings:Default"));
    }
}
