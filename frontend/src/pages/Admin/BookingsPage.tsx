import { useEffect, useState, useCallback } from 'react';
import { adminService } from '../../services/adminService';
import { BookingStatusBadge } from '../../components/BookingStatusBadge';
import type { AdminLocation, AdminBooking } from '../../types/admin';
import type { BookingStatus } from '../../types/booking';

const STATUS_OPTIONS = ['All', 'Pending', 'Won', 'Lost', 'Confirmed', 'Cancelled', 'Expired'];

export function BookingsPage() {
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [bookings, setBookings] = useState<AdminBooking[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [locationFilter, setLocationFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('All');
  const [fromFilter, setFromFilter] = useState('');
  const [toFilter, setToFilter] = useState('');

  const pageSize = 20;

  useEffect(() => {
    adminService.getLocations().then(setLocations).catch(() => {});
  }, []);

  const loadBookings = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminService.getBookings({
        locationId: locationFilter || undefined,
        status: statusFilter === 'All' ? undefined : statusFilter,
        from: fromFilter || undefined,
        to: toFilter || undefined,
        page,
        pageSize,
      });
      setBookings(res.bookings);
      setTotalCount(res.totalCount);
    } catch { setError('Failed to load bookings.'); }
    finally { setLoading(false); }
  }, [locationFilter, statusFilter, fromFilter, toFilter, page]);

  useEffect(() => { loadBookings(); }, [loadBookings]);

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">All Bookings</h1>
      <p className="mt-1 text-sm text-gray-500">{totalCount} booking{totalCount !== 1 ? 's' : ''} total</p>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}

      {/* Filters */}
      <div className="mt-4 flex flex-wrap gap-3">
        <select value={locationFilter} onChange={(e) => { setLocationFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm">
          <option value="">All Locations</option>
          {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>

        <select value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm">
          {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>

        <input type="date" value={fromFilter} onChange={(e) => { setFromFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm" placeholder="From" />
        <input type="date" value={toFilter} onChange={(e) => { setToFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm" placeholder="To" />
      </div>

      {/* Table */}
      <div className="mt-6 overflow-hidden rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Date</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Time</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Location</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">User</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Slot</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Status</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 bg-white">
            {loading ? (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-sm text-gray-400">Loading...</td></tr>
            ) : bookings.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-sm text-gray-400">No bookings found.</td></tr>
            ) : bookings.map((b) => (
              <tr key={b.id}>
                <td className="px-4 py-3 text-sm text-gray-900">{b.date}</td>
                <td className="px-4 py-3 text-sm text-gray-500">{b.timeSlot}</td>
                <td className="px-4 py-3 text-sm text-gray-500">{b.locationName}</td>
                <td className="px-4 py-3 text-sm">
                  <div className="text-gray-900">{b.userDisplayName}</div>
                  <div className="text-xs text-gray-400">{b.userEmail}</div>
                </td>
                <td className="px-4 py-3 text-sm text-gray-500">{b.parkingSlotNumber ?? '—'}</td>
                <td className="px-4 py-3"><BookingStatusBadge status={b.status as BookingStatus} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="mt-4 flex items-center justify-center gap-2">
          <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50">Previous</button>
          <span className="text-sm text-gray-600">Page {page} of {totalPages}</span>
          <button onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50">Next</button>
        </div>
      )}
    </div>
  );
}
