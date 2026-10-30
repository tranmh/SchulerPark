export interface UpdateProfileRequest {
  /** Phase 21: the server derives displayName from these two. Company is not editable here. */
  firstName: string;
  lastName: string;
  carLicensePlate: string | null;
  preferredLocationId: string | null;
  preferredSlotId: string | null;
  /** Optional; omitted leaves the stored notification language unchanged. */
  preferredLanguage?: string;
}

export interface DataExport {
  profile: {
    email: string;
    displayName: string;
    firstName: string | null;
    lastName: string | null;
    company: string | null;
    carLicensePlate: string | null;
    role: string;
    preferredLanguage: string;
    createdAt: string;
  };
  bookings: Array<{
    id: string;
    locationName: string;
    date: string;
    timeSlot: string;
    status: string;
    parkingSlotNumber: string | null;
    confirmedAt: string | null;
    createdAt: string;
  }>;
  lotteryHistory: Array<{
    locationName: string;
    date: string;
    timeSlot: string;
    won: boolean;
  }>;
  exportedAt: string;
}
