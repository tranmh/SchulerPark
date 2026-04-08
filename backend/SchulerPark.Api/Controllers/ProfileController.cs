namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Api.DTOs.Auth;
using SchulerPark.Api.DTOs.Profile;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProfileController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<UserDto>> GetProfile()
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null || user.DeletedAt != null) return NotFound();

        return Ok(new UserDto(user.Id, user.Email, user.DisplayName,
            user.CarLicensePlate, user.Role.ToString(), user.AzureAdObjectId != null));
    }

    [HttpPut]
    public async Task<ActionResult<UserDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null || user.DeletedAt != null) return NotFound();

        user.DisplayName = request.DisplayName;
        user.CarLicensePlate = request.CarLicensePlate;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(user.Id, user.Email, user.DisplayName,
            user.CarLicensePlate, user.Role.ToString(), user.AzureAdObjectId != null));
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
                user.Role.ToString(), user.CreatedAt),
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

        return Ok(new { message = "Account scheduled for deletion. Data will be permanently removed after 30 days." });
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
