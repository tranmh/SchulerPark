namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Infrastructure.Data;

/// <summary>Validates feedback and hands it to <see cref="IAdminNotifier"/>; mail only, nothing is stored.</summary>
public class FeedbackService : IFeedbackService
{
    public const int MaxCommentLength = 1000;
    public const int MaxPlateLength = 20;

    // Every submission mails all admins, so cap it per user. The global rate limiter runs
    // before authentication and only sees the (shared office) IP, hence the in-memory window.
    public const int MaxSubmissionsPerWindow = 5;
    public static readonly TimeSpan SubmissionWindow = TimeSpan.FromMinutes(10);

    private static readonly string[] PlateCountries = ["DE", "AT", "LU", "OTHER"];
    private static readonly string[] PlateTypes = ["Standard", "Historic", "Electric"];

    private readonly AppDbContext _db;
    private readonly IAdminNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly IMemoryCache _cache;

    public FeedbackService(AppDbContext db, IAdminNotifier notifier, TimeProvider time, IMemoryCache cache)
    {
        _db = db;
        _notifier = notifier;
        _time = time;
        _cache = cache;
    }

    public async Task SubmitAsync(Guid userId, FeedbackSubmission submission)
    {
        var comment = string.IsNullOrWhiteSpace(submission.Comment) ? null : submission.Comment.Trim();
        if (comment?.Length > MaxCommentLength)
            throw new ValidationException($"Comment exceeds {MaxCommentLength} characters.", "feedback_too_long");

        var counter = _cache.GetOrCreate($"feedback:{userId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SubmissionWindow;
            return new SubmissionCounter();
        })!;
        if (Interlocked.Increment(ref counter.Count) > MaxSubmissionsPerWindow)
            throw new ValidationException("Too many reports, please try again later.", "feedback_rate_limited");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null)
            ?? throw new NotFoundException("User not found.");

        if (submission.Category == FeedbackCategory.General)
        {
            if (comment is null)
                throw new ValidationException("Feedback needs a comment.", "feedback_comment_required");

            await _notifier.FeedbackReceivedAsync(new FeedbackReport(
                FeedbackCategory.General, user.DisplayName, user.Email, null,
                null, null, null, null, null, null, null, comment));
            return;
        }

        if (submission.BookingId is not { } bookingId)
            throw new ValidationException("A slot problem needs a booking.", "feedback_booking_not_assigned");

        var booking = await _db.Bookings
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new NotFoundException("Booking not found.");

        if (booking.UserId != userId)
            throw new ForbiddenException("You can only report problems with your own bookings.");

        if (booking.Status is not (BookingStatus.Won or BookingStatus.Confirmed) || booking.ParkingSlot is null)
            throw new ValidationException("The booking has no assigned spot.", "feedback_booking_not_assigned");

        if (booking.Date != DeadlineHelper.BerlinToday(_time.GetUtcNow().UtcDateTime))
            throw new ValidationException("Problems can only be reported for today's booking.", "feedback_booking_not_today");

        string? plateCountry = null, plateType = null, plateText = null;
        if (submission.Category == FeedbackCategory.SlotOccupied && !string.IsNullOrWhiteSpace(submission.PlateText))
        {
            plateText = submission.PlateText.Trim();
            if (plateText.Length > MaxPlateLength)
                throw new ValidationException($"Licence plate exceeds {MaxPlateLength} characters.", "feedback_too_long");
            plateCountry = PlateCountries.Contains(submission.PlateCountry) ? submission.PlateCountry : "OTHER";
            plateType = PlateTypes.Contains(submission.PlateType) ? submission.PlateType : "Standard";
        }

        await _notifier.FeedbackReceivedAsync(new FeedbackReport(
            submission.Category, user.DisplayName, user.Email, user.CarLicensePlate,
            booking.Location.Name, booking.ParkingSlot.SlotNumber, booking.Date, booking.TimeSlot,
            plateCountry, plateType, plateText, comment));
    }

    private sealed class SubmissionCounter
    {
        public int Count;
    }
}
