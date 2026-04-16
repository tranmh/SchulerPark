import api from './api';
import type { Booking, CreateBookingRequest, CreateWeekBookingRequest, MyBookingsResponse, BookingFilters, WeekBookingResponse } from '../types/booking';

export const bookingService = {
  create: (data: CreateBookingRequest) =>
    api.post<Booking>('/bookings', data).then(r => r.data),

  createWeek: (data: CreateWeekBookingRequest) =>
    api.post<WeekBookingResponse>('/bookings/week', data).then(r => r.data),

  getMyBookings: (filters?: BookingFilters) =>
    api.get<MyBookingsResponse>('/bookings/my', { params: filters }).then(r => r.data),

  cancel: (bookingId: string) =>
    api.delete(`/bookings/${bookingId}`),

  confirm: (bookingId: string) =>
    api.post<Booking>(`/bookings/${bookingId}/confirm`).then(r => r.data),
};
