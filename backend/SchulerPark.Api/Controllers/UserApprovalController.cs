namespace SchulerPark.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SchulerPark.Api.DTOs.Admin;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

/// <summary>
/// Phase 18: accept/decline external registrations. Deliberately AdminOnly (not
/// SuperAdminOnly like the user CRUD in UsersAdminController) — approving a
/// visitor's account is routine admin work.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "AdminOnly")]
public class UserApprovalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly AppSettings _appSettings;

    public UserApprovalController(AppDbContext db, IEmailService emailService, IOptions<AppSettings> appSettings)
    {
        _db = db;
        _emailService = emailService;
        _appSettings = appSettings.Value;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> ListPending()
    {
        var users = await _db.Users
            .Where(u => u.ApprovalStatus == ApprovalStatus.Pending && u.DeletedAt == null)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new PendingUserDto(u.Id, u.Email, u.DisplayName, u.EmailVerified, u.CreatedAt))
            .ToListAsync();

        return Ok(new { users, totalCount = users.Count });
    }

    [HttpPost("{id:guid}/approval")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] ApprovalDecisionRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user == null) return NotFound();

        if (user.ApprovalStatus != ApprovalStatus.Pending)
            throw new ValidationException("Only pending users can be approved or rejected.");

        if (request.Approve == true)
        {
            user.ApprovalStatus = ApprovalStatus.Approved;
            user.ApprovedAt = DateTime.UtcNow;
            user.ApprovedByUserId = GetUserId();
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var loginLink = $"{_appSettings.BaseUrl.TrimEnd('/')}/login";
            await _emailService.SendAccountApprovedAsync(user.Email, user.DisplayName, loginLink, user.PreferredLanguage);
        }
        else
        {
            // Keep the row as Rejected (not deleted): the address can't silently
            // re-register, and at login the account behaves like bad credentials.
            user.ApprovalStatus = ApprovalStatus.Rejected;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _emailService.SendAccountRejectedAsync(user.Email, user.DisplayName, user.PreferredLanguage);
        }

        return Ok(new PendingUserDto(user.Id, user.Email, user.DisplayName, user.EmailVerified, user.CreatedAt));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
