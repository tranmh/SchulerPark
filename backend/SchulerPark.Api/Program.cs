using Microsoft.EntityFrameworkCore;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SchulerPark API",
        Version = "v1",
        Description = "Parking Slot Booking System API"
    });
});
builder.Services.AddControllers();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// TODO Phase 3: Add Authentication (Azure AD + JWT)
// TODO Phase 5: Add Hangfire

var app = builder.Build();

// Auto-migrate and seed in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

// TODO Phase 3: app.UseAuthentication();
// TODO Phase 3: app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// SPA fallback: serve index.html for non-API, non-file routes
app.MapFallbackToFile("index.html");

await app.RunAsync();
