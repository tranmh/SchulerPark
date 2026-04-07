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

// TODO Phase 2: Add DbContext (EF Core + PostgreSQL)
// TODO Phase 3: Add Authentication (Azure AD + JWT)
// TODO Phase 5: Add Hangfire

var app = builder.Build();

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

app.Run();
