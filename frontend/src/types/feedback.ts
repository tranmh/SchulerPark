export type FeedbackCategory = 'General' | 'SlotOccupied' | 'SlotUnusable' | 'SlotNotFound' | 'ChargerFault';

export type SlotProblemCategory = Exclude<FeedbackCategory, 'General'>;

export type PlateCountry = 'DE' | 'AT' | 'LU' | 'OTHER';

export type PlateType = 'Standard' | 'Historic' | 'Electric';

export interface SubmitFeedbackRequest {
  category: FeedbackCategory;
  bookingId?: string;
  plateCountry?: PlateCountry;
  plateType?: PlateType;
  plateText?: string;
  comment?: string;
}
