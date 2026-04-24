export type TimeSlot = 'Morning' | 'Afternoon';

export type BookingStatus = 'Pending' | 'Won' | 'Lost' | 'Confirmed' | 'Cancelled' | 'Expired';

export interface Location {
  id: string;
  name: string;
  address: string;
  totalSlots: number;
}

export interface ParkingSlot {
  id: string;
  slotNumber: string;
  label: string | null;
  isActive: boolean;
  gridRow: number | null;
  gridColumn: number | null;
}

export interface BlockedDay {
  id: string;
  date: string;
  parkingSlotId: string | null;
  reason: string | null;
}

export interface Booking {
  id: string;
  locationId: string;
  locationName: string;
  parkingSlotId: string | null;
  parkingSlotNumber: string | null;
  date: string;
  timeSlot: TimeSlot;
  status: BookingStatus;
  confirmedAt: string | null;
  createdAt: string;
  confirmationDeadline: string | null;
  fallbackReason: string | null;
}

export interface Availability {
  date: string;
  timeSlot: TimeSlot;
  availableSlots: number;
  totalSlots: number;
  bookingCount: number;
}

export interface CreateBookingRequest {
  locationId: string | null;
  date: string;
  timeSlot: TimeSlot;
}

export interface MyBookingsResponse {
  bookings: Booking[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CreateWeekBookingRequest {
  locationId: string | null;
  weekStartDate: string;
  timeSlot: TimeSlot;
}

export interface SkippedDay {
  date: string;
  reason: string;
}

export interface WeekBookingResponse {
  createdBookings: Booking[];
  skippedDays: SkippedDay[];
}

export interface BookingFilters {
  page?: number;
  pageSize?: number;
  status?: BookingStatus;
  from?: string;
  to?: string;
}
