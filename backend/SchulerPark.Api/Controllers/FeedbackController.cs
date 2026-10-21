namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchulerPark.Api.DTOs.Feedback;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;

[ApiController]
[Route("api/feedback")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly IFeedbackService _feedback;

    public FeedbackController(IFeedbackService feedback)
    {
        _feedback = feedback;
    }

    /// <summary>Mails a slot problem or general feedback to all admins. 202 once handed to the mailer.</summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest request)
    {
        if (!Enum.TryParse<FeedbackCategory>(request.Category, ignoreCase: true, out var category)
            || !Enum.IsDefined(category) || int.TryParse(request.Category, out _))
            throw new ValidationException("Unknown feedback category.", "feedback_invalid_category");

        await _feedback.SubmitAsync(GetUserId(), new FeedbackSubmission(
            category, request.BookingId, request.PlateCountry, request.PlateType, request.PlateText, request.Comment));
        return Accepted();
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
