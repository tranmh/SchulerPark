using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove DbContext registrations (replace PostgreSQL with InMemory)
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

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
