export interface UpdateProfileRequest {
  displayName: string;
  carLicensePlate: string | null;
}

export interface DataExport {
  profile: {
    email: string;
    displayName: string;
    carLicensePlate: string | null;
    role: string;
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
