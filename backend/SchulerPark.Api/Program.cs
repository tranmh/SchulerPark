using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi.Models;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Data.Seed;
using SchulerPark.Infrastructure.Jobs;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Infrastructure.Services.Placement;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SchulerPark API",
        Version = "v1",
        Description = "Parking Slot Booking System API"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddControllers();

// Bug #49: cache the per-request account-active check (below) so it doesn't hit the DB on
// every authenticated call.
builder.Services.AddMemoryCache();

// Database
var connectionString = builder.Configuration.GetConnectionString("Default");

// Bug #16: refuse to start with a missing connection string or the committed 'changeme'
// default password outside local development — a forgotten DB_PASSWORD must fail loudly,
// not silently run Postgres with a publicly known password.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")
    && SchulerPark.Api.StartupGuards.IsUnsafeDbConnectionString(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Default is missing or uses the default 'changeme' password. " +
        "Set a strong DB_PASSWORD / ConnectionStrings__Default before starting in this environment.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Settings
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AzureAdSettings>(builder.Configuration.GetSection("AzureAd"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<VapidSettings>(builder.Configuration.GetSection("Vapid"));

// DataProtection: in production, persist keys to a mounted volume (/keys) so they
// survive container recreation. Without this, ASP.NET stores keys in the container's
// ephemeral filesystem and every deploy regenerates them, invalidating all auth
// cookies / antiforgery tokens. SetApplicationName keeps the key ring stable and
// shared across scaled app replicas. Dev keeps the framework defaults (/keys is not
// writable on a dev box).
if (builder.Environment.IsProduction())
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/keys"))
        .SetApplicationName("LouisE");
}

// Auth services
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<AzureAdTokenValidator>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// JWT authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

// Refuse to start with a missing, placeholder, or too-short HMAC secret outside
// local development — an empty/known key lets anyone forge SuperAdmin tokens.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    if (string.IsNullOrWhiteSpace(jwtSettings.Secret)
        || jwtSettings.Secret.Contains("CHANGE_THIS", StringComparison.OrdinalIgnoreCase)
        || Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:Secret is missing, a placeholder, or shorter than 32 bytes. " +
            "Set a strong JWT_SECRET before starting in this environment.");
    }
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };

        // Access tokens live 60 minutes; without this check a disabled or
        // DSGVO-deleted account keeps API access until its token expires.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(sub, out var userId))
                {
                    context.Fail("Invalid subject claim.");
                    return;
                }

                // Bug #49: this runs on EVERY authenticated request. Cache the result briefly
                // (IMemoryCache, 30s TTL) so a valid-token flood doesn't translate into a DB
                // round-trip per call. A disabled account then loses access within the TTL —
                // the same bounded window the 60-minute access token already tolerates. Still
                // fails closed (a null/absent user → active == false → context.Fail).
                var services = context.HttpContext.RequestServices;
                var cache = services.GetRequiredService<IMemoryCache>();
                var active = await cache.GetOrCreateAsync(SchulerPark.Api.Auth.UserActiveCache.Key(userId), entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = SchulerPark.Api.Auth.UserActiveCache.Ttl;
                    var db = services.GetRequiredService<AppDbContext>();
                    return db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null);
                });
                if (!active)
                    context.Fail("Account is disabled or deleted.");
            }
        };
    });

// Rate limiting (H3): strict per-IP window on the auth endpoints (password
// brute-force, credential stuffing, mass registration), sane global default
// elsewhere. Caddy forwards the client IP via X-Forwarded-For, which
// UseForwardedHeaders has already applied by the time the limiter runs.
// Limits are config-overridable: integration tests and the Playwright E2E run
// hammer the auth endpoints from one IP, so Development/Testing relax them
// (appsettings.Development.json / test factory) while production keeps the
// strict defaults.
var authPermitLimit = builder.Configuration.GetValue("RateLimit:AuthPermitLimit", 10);
var globalPermitLimit = builder.Configuration.GetValue("RateLimit:GlobalPermitLimit", 300);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Authorization policies. Role hierarchy is inclusive: SuperAdmin satisfies AdminOnly.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin", "SuperAdmin"));
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("SuperAdmin"));
});

// Phase 4: Booking services
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ILocationService, LocationService>();

// Phase 5: Lottery services
builder.Services.AddScoped<ILotteryService, LotteryService>();
builder.Services.AddScoped<ISlotDistanceMetric, ManhattanDistanceMetric>();
builder.Services.AddScoped<ISlotPlacer, PreferenceAwareSlotPlacer>();
builder.Services.AddScoped<IDirectAssignmentService, DirectAssignmentService>();

// Waitlist service
builder.Services.AddScoped<IWaitlistService, WaitlistService>();

// Push notification service
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();

// Phase 7: Email service
builder.Services.AddScoped<IEmailService, EmailService>();

// Hangfire
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default"))));
builder.Services.AddHangfireServer();

var app = builder.Build();

// Auto-migrate database (all environments — EF Core migrations are idempotent).
// Skip in the Testing environment, which uses the InMemory provider.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Bootstrap a SuperAdmin on first startup (no users yet). Runs in all
    // non-Testing environments so production gets a credential file written
    // to disk on first boot. Idempotent — does nothing once any user exists.
    await BootstrapAdmin.BootstrapAsync(app.Services);

    if (app.Environment.IsDevelopment())
    {
        await SeedData.SeedAsync(app.Services);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<SchulerPark.Api.Middleware.ExceptionHandlingMiddleware>();

// Trust reverse proxy headers (Caddy). KnownIPNetworks/KnownProxies default to
// loopback only, which would silently ignore Caddy's X-Forwarded-For (it arrives
// from a compose-network IP) — clear them; app:8080 is only reachable from the
// compose network, so the immediate hop is trusted.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Must run after UseForwardedHeaders so per-IP partitions see the client IP,
// not Caddy's container IP (which would pool all users into one bucket).
app.UseRateLimiter();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
    {
        Authorization = []
    }).AllowAnonymous();
}

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Register Hangfire recurring jobs (use DI-based manager, not static API).
// Skip in Testing — the factory swaps Hangfire out and there's no Postgres backend.
if (!app.Environment.IsEnvironment("Testing"))
{
    var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
    var berlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    // Lottery recurring job: 10 PM Europe/Berlin daily
    jobManager.AddOrUpdate<LotteryJob>(
        "daily-lottery",
        job => job.ExecuteAsync(),
        "0 22 * * *",
        new RecurringJobOptions { TimeZone = berlinTz });

    // Confirmation expiry job: every hour
    jobManager.AddOrUpdate<ConfirmationExpiryJob>(
        "confirmation-expiry",
        job => job.ExecuteAsync(),
        "0 * * * *",
        new RecurringJobOptions { TimeZone = berlinTz });

    // Data retention job: weekly, Sunday 2 AM
    jobManager.AddOrUpdate<DataRetentionJob>(
        "data-retention",
        job => job.ExecuteAsync(),
        "0 2 * * 0",
        new RecurringJobOptions { TimeZone = berlinTz });
}

// SPA fallback: serve index.html for non-API, non-file routes
app.MapFallbackToFile("index.html");

await app.RunAsync();

// Make the implicit Program class accessible for integration tests
public partial class Program { }
