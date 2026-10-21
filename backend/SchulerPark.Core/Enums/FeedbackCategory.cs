namespace SchulerPark.Core.Enums;

/// <summary>What a user reports through the feedback dialogs; everything except General refers to an assigned slot.</summary>
public enum FeedbackCategory
{
    General = 0,
    SlotOccupied = 1,
    SlotUnusable = 2,
    SlotNotFound = 3,
    ChargerFault = 4
}
