export interface AdminLocation {
  id: string;
  name: string;
  address: string;
  isActive: boolean;
  defaultAlgorithm: string;
  totalSlots: number;
  activeSlots: number;
}

export interface CreateLocationRequest {
  name: string;
  address: string;
}

export interface UpdateLocationRequest {
  name: string;
  address: string;
  isActive: boolean;
}

export interface AdminSlot {
  id: string;
  locationId: string;
  slotNumber: string;
  label: string | null;
  isActive: boolean;
}

export interface CreateSlotRequest {
  locationId: string;
  slotNumber: string;
  label?: string;
}

export interface UpdateSlotRequest {
  slotNumber: string;
  label?: string;
  isActive: boolean;
}

export interface AdminBlockedDay {
  id: string;
  locationId: string;
  locationName: string;
  parkingSlotId: string | null;
  slotNumber: string | null;
  date: string;
  reason: string | null;
  createdAt: string;
}

export interface CreateBlockedDayRequest {
  locationId: string;
  parkingSlotId?: string;
  date: string;
  reason?: string;
}

export interface AdminBooking {
  id: string;
  userId: string;
  userEmail: string;
  userDisplayName: string;
  locationId: string;
  locationName: string;
  parkingSlotId: string | null;
  parkingSlotNumber: string | null;
  date: string;
  timeSlot: string;
  status: string;
  confirmedAt: string | null;
  createdAt: string;
}

export interface LotteryRun {
  id: string;
  locationId: string;
  locationName: string;
  date: string;
  timeSlot: string;
  algorithm: string;
  ranAt: string;
  totalBookings: number;
  availableSlots: number;
}

export interface PagedResponse<T> {
  totalCount: number;
  page: number;
  pageSize: number;
  [key: string]: T[] | number;
}
