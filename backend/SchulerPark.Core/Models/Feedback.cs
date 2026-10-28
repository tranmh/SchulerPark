namespace SchulerPark.Core.Models;

using SchulerPark.Core.Enums;

/// <summary>
/// A user's feedback as submitted. Slot categories need <see cref="BookingId"/> (today's
/// assigned booking); the plate fields only count for <see cref="FeedbackCategory.SlotOccupied"/>.
/// </summary>
public record FeedbackSubmission(
    FeedbackCategory Category,
    Guid? BookingId,
    string? PlateCountry,
    string? PlateType,
    string? PlateText,
    string? Comment);

/// <summary>The validated report the admins are mailed. Slot fields are null for general feedback.</summary>
public record FeedbackReport(
    FeedbackCategory Category,
    string ReporterName,
    string ReporterEmail,
    string? ReporterPlate,
    string? LocationName,
    string? SlotNumber,
    DateOnly? Date,
    TimeSlot? TimeSlot,
    string? PlateCountry,
    string? PlateType,
    string? PlateText,
    string? Comment);
