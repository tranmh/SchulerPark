using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;

namespace SchulerPark.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    /// <summary>Captured outbound emails (verification links etc.).</summary>
    public CapturingEmailService Emails =>
        (CapturingEmailService)Services.GetRequiredService<IEmailService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Configure AzureAd so /api/auth/azure-callback is enabled; the real
        // validator is swapped for FakeAzureAdTokenValidator below.
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");

        // Tests share a single connection with no client IP — don't rate-limit them.
        builder.UseSetting("RateLimit:AuthPermitLimit", "1000000");
        builder.UseSetting("RateLimit:GlobalPermitLimit", "1000000");

        builder.ConfigureServices(services =>
        {
            // Capture emails instead of SMTP; tests read verification links from here.
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, CapturingEmailService>();

            // Fake Azure AD token validation ("fake|oid|email|name" tokens).
            services.RemoveAll<AzureAdTokenValidator>();
            services.AddSingleton<AzureAdTokenValidator, FakeAzureAdTokenValidator>();

            // Remove DbContext registrations (replace PostgreSQL with InMemory)
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
                options
                    .UseInMemoryDatabase(_dbName)
                    // BookingService now opens an explicit transaction for week booking (#53).
                    // InMemory has no transactions; treat BeginTransaction/Commit as no-ops here
                    // instead of throwing. Real transactional behaviour is covered by the
                    // Testcontainers Postgres tests.
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            // Remove ALL Hangfire hosted services to avoid 90s shutdown timeout
            var hangfireHosted = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .Where(d =>
                {
                    var typeName = (d.ImplementationType ?? d.ImplementationFactory?.Method.ReturnType)?.FullName ?? "";
                    return typeName.Contains("Hangfire", StringComparison.OrdinalIgnoreCase)
                        || typeName.Contains("BackgroundJobServer", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            foreach (var d in hangfireHosted)
                services.Remove(d);

            // Set Hangfire global storage so static RecurringJob calls don't fail
            GlobalConfiguration.Configuration.UseMemoryStorage();
        });
    }
}
