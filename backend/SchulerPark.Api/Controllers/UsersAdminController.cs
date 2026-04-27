namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Api.DTOs.Admin;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "SuperAdminOnly")]
public class UsersAdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersAdminController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email.ToLower().Contains(s) ||
                u.DisplayName.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(role)
            && Enum.TryParse<UserRole>(role, ignoreCase: true, out var r))
        {
            query = query.Where(u => u.Role == r);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = users.Select(u => new AdminUserDto(
            u.Id, u.Email, u.DisplayName,
            u.Role.ToString(),
            u.DeletedAt != null,
            u.AzureAdObjectId != null,
            u.CreatedAt)).ToList();

        return Ok(new { users = dtos, totalCount, page, pageSize });
    }

    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<AdminUserDto>> UpdateRole(Guid id, [FromBody] UpdateUserRoleRequest request)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var newRole))
            throw new ValidationException("Role must be 'User', 'Admin', or 'SuperAdmin'.");

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (user.Id == GetUserId())
            throw new ValidationException("You cannot change your own role.");

        if (user.Role == newRole)
            return Ok(ToDto(user));

        if (user.Role == UserRole.SuperAdmin && newRole != UserRole.SuperAdmin)
        {
            var remainingSuperAdmins = await _db.Users
                .CountAsync(u => u.Role == UserRole.SuperAdmin
                                 && u.DeletedAt == null
                                 && u.Id != user.Id);
            if (remainingSuperAdmins == 0)
                throw new ValidationException("Cannot demote the last remaining SuperAdmin.");
        }

        user.Role = newRole;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ToDto(user));
    }

    [HttpPut("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (user.Id == GetUserId())
            throw new ValidationException("You cannot disable your own account.");

        if (user.Role == UserRole.SuperAdmin)
        {
            var remainingSuperAdmins = await _db.Users
                .CountAsync(u => u.Role == UserRole.SuperAdmin
                                 && u.DeletedAt == null
                                 && u.Id != user.Id);
            if (remainingSuperAdmins == 0)
                throw new ValidationException("Cannot disable the last remaining SuperAdmin.");
        }

        if (user.DeletedAt == null)
        {
            user.DeletedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            var tokens = await _db.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ToListAsync();
            foreach (var token in tokens)
                token.RevokedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        return Ok(ToDto(user));
    }

    [HttpPut("{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (user.DeletedAt != null)
        {
            user.DeletedAt = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(ToDto(user));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (user.Id == GetUserId())
            throw new ValidationException("You cannot delete your own account.");

        if (user.Role == UserRole.SuperAdmin)
        {
            var remainingSuperAdmins = await _db.Users
                .CountAsync(u => u.Role == UserRole.SuperAdmin
                                 && u.DeletedAt == null
                                 && u.Id != user.Id);
            if (remainingSuperAdmins == 0)
                throw new ValidationException("Cannot delete the last remaining SuperAdmin.");
        }

        // BlockedDay.BlockedByUserId has DeleteBehavior.Restrict — clear those first.
        // Using Remove (not ExecuteDelete) keeps this provider-agnostic for tests.
        var blockedDays = await _db.BlockedDays
            .Where(b => b.BlockedByUserId == user.Id)
            .ToListAsync();
        if (blockedDays.Count > 0)
            _db.BlockedDays.RemoveRange(blockedDays);

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static AdminUserDto ToDto(Core.Entities.User user) => new(
        user.Id, user.Email, user.DisplayName,
        user.Role.ToString(),
        user.DeletedAt != null,
        user.AzureAdObjectId != null,
        user.CreatedAt);

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
