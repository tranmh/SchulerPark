namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SchulerPark.Api.Auth;
using SchulerPark.Api.DTOs.Auth;
using SchulerPark.Api.DTOs.Profile;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAuthService _authService;
    private readonly IBookingLifecycleService _lifecycle;
    private readonly JwtSettings _jwtSettings;

    public ProfileController(AppDbContext db, IMemoryCache cache, IAuthService authService,
        IBookingLifecycleService lifecycle, IOptions<JwtSettings> jwtSettings)
    {
        _db = db;
        _cache = cache;
        _authService = authService;
        _lifecycle = lifecycle;
        _jwtSettings = jwtSettings.Value;
    }

    [HttpGet]
    public async Task<ActionResult<UserDto>> GetProfile()
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null || user.DeletedAt != null) return NotFound();

        return Ok(UserDto.From(user));
    }

    [HttpPut]
    public async Task<ActionResult<UserDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null || user.DeletedAt != null) return NotFound();

        if (request.PreferredLocationId.HasValue)
        {
            var exists = await _db.Locations.AnyAsync(l =>
                l.Id == request.PreferredLocationId.Value && l.IsActive);
            if (!exists)
                throw new ValidationException("Preferred location not found or inactive.");
        }

        if (request.PreferredSlotId.HasValue)
        {
            if (!request.PreferredLocationId.HasValue)
                throw new ValidationException("Preferred slot requires a preferred location.");

            var slot = await _db.ParkingSlots.FirstOrDefaultAsync(s =>
                s.Id == request.PreferredSlotId.Value && s.IsActive);
            if (slot is null)
                throw new ValidationException("Preferred slot not found or inactive.");
            if (slot.LocationId != request.PreferredLocationId.Value)
                throw new ValidationException("Preferred slot does not belong to the preferred location.");
        }

        user.DisplayName = request.DisplayName;
        user.CarLicensePlate = request.CarLicensePlate;
        user.PreferredLocationId = request.PreferredLocationId;
        user.PreferredSlotId = request.PreferredSlotId;
        if (request.PreferredLanguage != null)
            user.PreferredLanguage = ParseLanguage(request.PreferredLanguage);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(UserDto.From(user));
    }

    /// <summary>
    /// Sets the notification language. The frontend calls this whenever the signed-in
    /// user's UI language differs from the stored one (toggle or login on another device),
    /// so emails and push notifications follow the language the app is used in.
    /// </summary>
    [HttpPut("language")]
    public async Task<ActionResult<UserDto>> UpdateLanguage([FromBody] UpdateLanguageRequest request)
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null || user.DeletedAt != null) return NotFound();

        var language = ParseLanguage(request.Language);
        if (user.PreferredLanguage != language)
        {
            user.PreferredLanguage = language;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(UserDto.From(user));
    }

    /// <summary>
    /// Phase 20 WP2: change the local password. Every other session is revoked; the
    /// response carries a fresh token pair (and refresh cookie) so this one continues.
    /// Codes: <c>password_incorrect</c>, <c>password_not_set</c>, <c>password_too_weak</c>.
    /// </summary>
    [HttpPost("change-password")]
    public async Task<ActionResult<AuthResponse>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var (user, accessToken, refreshToken) = await _authService.ChangePasswordAsync(
            GetUserId(), request.CurrentPassword, request.NewPassword,
            HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString());

        RefreshTokenCookie.Set(Response, refreshToken, _jwtSettings);

        return Ok(new AuthResponse(
            accessToken,
            DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
            UserDto.From(user)));
    }

    private static string ParseLanguage(string language)
    {
        if (!Localization.IsSupported(language))
            throw new ValidationException("Language must be 'de' or 'en'.", "unsupported_language");
        return language.ToLowerInvariant();
    }

    [HttpGet("data-export")]
    public async Task<ActionResult<DataExportDto>> ExportData()
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.DeletedAt != null) return NotFound();

        var bookings = await _db.Bookings
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Date)
            .ToListAsync();

        var lotteryHistory = await _db.LotteryHistories
            .Include(h => h.Location)
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.Date)
            .ToListAsync();

        var export = new DataExportDto(
            Profile: new UserProfileExport(
                user.Email, user.DisplayName, user.CarLicensePlate,
                user.Role.ToString(), user.PreferredLanguage, user.CreatedAt),
            Bookings: bookings.Select(b => new BookingExport(
                b.Id, b.Location.Name, b.Date, b.TimeSlot.ToString(),
                b.Status.ToString(), b.ParkingSlot?.SlotNumber,
                b.ConfirmedAt, b.CreatedAt)).ToList(),
            LotteryHistory: lotteryHistory.Select(h => new LotteryHistoryExport(
                h.Location.Name, h.Date, h.TimeSlot.ToString(), h.Won)).ToList(),
            ExportedAt: DateTime.UtcNow);

        return Ok(export);
    }

    [HttpDelete("data")]
    public async Task<IActionResult> RequestDeletion()
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null || user.DeletedAt != null) return NotFound();

        // Soft-delete: set DeletedAt, revoke all refresh tokens
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync();
        foreach (var token in tokens)
            token.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Bug #49/#4: evict the cached "active" result so the existing access token is rejected
        // on its very next request, not up to the cache TTL later.
        UserActiveCache.Evict(_cache, user.Id);

        // WP1 3.3: the account is gone, so its upcoming bookings must not keep holding slots.
        await _lifecycle.ReleaseUserBookingsAsync(user.Id, BookingReleaseReason.UserDeleted);

        return Ok(new { message = "Account scheduled for deletion. Data will be permanently removed after 30 days." });
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
