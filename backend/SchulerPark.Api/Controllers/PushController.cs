namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchulerPark.Api.DTOs.Push;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

[ApiController]
[Route("api/push")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly VapidSettings _vapid;
    private readonly IPushNotificationService _push;

    public PushController(AppDbContext db, IOptions<VapidSettings> vapid, IPushNotificationService push)
    {
        _db = db;
        _vapid = vapid.Value;
        _push = push;
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

    // The endpoint URL is a bearer capability — accept it in the body, not the
    // query string, so it never lands in access logs.
    [HttpDelete("subscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribePushRequest request)
    {
        var userId = GetUserId();
        var deleted = await _db.PushSubscriptions
            .Where(ps => ps.UserId == userId && ps.Endpoint == request.Endpoint)
            .ExecuteDeleteAsync();

        return deleted > 0 ? NoContent() : NotFound();
    }

    /// <summary>
    /// Manual end-to-end check: pushes a test notification to every device the
    /// caller has subscribed. 200 when at least one push service accepted the
    /// message, 404 when the caller has no subscription (or push is not configured),
    /// 502 when subscriptions exist but every delivery was rejected.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(PushTestResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PushTestResponse>> SendTest()
    {
        if (!_vapid.IsConfigured)
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = "Push notifications are not configured.", Status = 404 });

        var result = await _push.SendTestAsync(GetUserId());
        var body = new PushTestResponse(result.Subscriptions, result.Delivered, result.Removed, result.Failed);

        if (result.Subscriptions == 0)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = "No push subscription is registered for this account. Enable push notifications first.",
                Status = 404
            });

        if (result.Delivered == 0)
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Push delivery failed",
                Detail = $"None of the {result.Subscriptions} subscription(s) accepted the message " +
                         $"({result.Removed} expired and removed, {result.Failed} failed).",
                Status = 502
            });

        return Ok(body);
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
