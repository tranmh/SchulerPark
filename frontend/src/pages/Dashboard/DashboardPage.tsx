import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';
import { BookingStatusBadge } from '../../components/BookingStatusBadge';
import type { Booking, Location } from '../../types/booking';

export function DashboardPage() {
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [locations, setLocations] = useState<Location[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const load = async () => {
      try {
        const today = new Date().toISOString().split('T')[0];
        const [bookingRes, locs] = await Promise.all([
          bookingService.getMyBookings({ pageSize: 5, from: today }),
          locationService.getLocations(),
        ]);
        setBookings(bookingRes.bookings);
        setLocations(locs);
      } catch {
        // Silently handle — empty state will show
      } finally {
        setLoading(false);
      }
    };
    load();
  }, []);

  if (loading) {
    return <div className="flex h-64 items-center justify-center text-gray-400">Loading...</div>;
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
      <p className="mt-1 text-gray-500">Parking slot booking system for Schuler locations.</p>

      {/* Upcoming Bookings */}
      <section className="mt-8">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900">Upcoming Bookings</h2>
          <Link to="/my-bookings" className="text-sm text-blue-600 hover:text-blue-800">
            View all
          </Link>
        </div>

        {bookings.length === 0 ? (
          <div className="mt-4 rounded-lg border-2 border-dashed border-gray-200 p-8 text-center">
            <p className="text-gray-500">No upcoming bookings.</p>
            <Link
              to="/booking"
              className="mt-2 inline-block text-sm font-medium text-blue-600 hover:text-blue-800"
            >
              Book a parking spot
            </Link>
          </div>
        ) : (
          <div className="mt-4 space-y-3">
            {bookings.map((b) => (
              <div key={b.id} className="flex items-center justify-between rounded-lg border border-gray-200 bg-white p-4">
                <div>
                  <div className="font-medium text-gray-900">
                    {new Date(b.date + 'T00:00:00').toLocaleDateString('de-DE', {
                      weekday: 'short', day: 'numeric', month: 'short', year: 'numeric',
                    })}
                    {' '}&middot;{' '}{b.timeSlot}
                  </div>
                  <div className="text-sm text-gray-500">{b.locationName}</div>
                  {b.parkingSlotNumber && (
                    <div className="text-sm text-gray-500">Slot: {b.parkingSlotNumber}</div>
                  )}
                  {b.status === 'Won' && b.confirmationDeadline && (
                    <div className="mt-1 text-xs text-amber-600 font-medium">
                      Confirm by {new Date(b.confirmationDeadline).toLocaleTimeString('de-DE', {
                        hour: '2-digit', minute: '2-digit',
                      })}
                    </div>
                  )}
                </div>
                <BookingStatusBadge status={b.status} />
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Quick Book */}
      <section className="mt-8">
        <h2 className="text-lg font-semibold text-gray-900">Quick Book</h2>
        <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {locations.map((loc) => (
            <Link
              key={loc.id}
              to={`/booking?location=${loc.id}`}
              className="rounded-lg border border-gray-200 bg-white p-4 transition-shadow hover:shadow-md"
            >
              <div className="font-semibold text-gray-900">{loc.name}</div>
              <div className="mt-1 text-sm text-gray-500">{loc.address}</div>
              <div className="mt-3 text-sm font-medium text-blue-600">
                {loc.totalSlots} slot{loc.totalSlots !== 1 ? 's' : ''} &rarr;
              </div>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
}
