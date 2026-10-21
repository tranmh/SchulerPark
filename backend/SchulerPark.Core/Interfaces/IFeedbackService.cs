namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Models;

public interface IFeedbackService
{
    /// <summary>
    /// Validates the submission and mails it to every active Admin/SuperAdmin. Nothing is stored.
    /// Slot problems must refer to the caller's own Won/Confirmed booking for today (Berlin).
    /// </summary>
    Task SubmitAsync(Guid userId, FeedbackSubmission submission);
}
