namespace SchulerPark.Api.DTOs.Feedback;

/// <summary>
/// Category is a <c>FeedbackCategory</c> name (General, SlotOccupied, SlotUnusable, SlotNotFound,
/// ChargerFault). PlateCountry: DE/AT/LU/OTHER; PlateType: Standard/Historic/Electric.
/// </summary>
public record SubmitFeedbackRequest(
    string Category,
    Guid? BookingId,
    string? PlateCountry,
    string? PlateType,
    string? PlateText,
    string? Comment);
