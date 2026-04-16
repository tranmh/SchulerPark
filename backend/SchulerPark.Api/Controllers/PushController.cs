namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchulerPark.Api.DTOs.Push;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/push")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly VapidSettings _vapid;

    public PushController(AppDbContext db, IOptions<VapidSettings> vapid)
    {
        _db = db;
        _vapid = vapid.Value;
    }

    [HttpGet("vapid-public-key")]
    public ActionResult<object> GetVapidPublicKey()
    {
        if (!_vapid.IsConfigured)
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Push notifications are not configured.", Status = 404 });

        return Ok(new { publicKey = _vapid.PublicKey });
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribePushRequest request)
    {
        var userId = GetUserId();

        var existing = await _db.PushSubscriptions
            .FirstOrDefaultAsync(ps => ps.UserId == userId && ps.Endpoint == request.Endpoint);

        if (existing != null)
        {
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
        }
        else
        {
            _db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("subscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string endpoint)
    {
        var userId = GetUserId();
        var deleted = await _db.PushSubscriptions
            .Where(ps => ps.UserId == userId && ps.Endpoint == endpoint)
            .ExecuteDeleteAsync();

        return deleted > 0 ? NoContent() : NotFound();
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
