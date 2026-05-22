import { useEffect, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import { BookingStatusBadge } from '../../components/BookingStatusBadge';
import type { AdminLocation, AdminBooking } from '../../types/admin';
import type { BookingStatus } from '../../types/booking';

const STATUS_OPTIONS = ['All', 'Pending', 'Won', 'Lost', 'Confirmed', 'Cancelled', 'Expired'];

export function BookingsPage() {
  const { t } = useTranslation();
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [bookings, setBookings] = useState<AdminBooking[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
    } catch {
      setError('Failed to load bookings.');
    } finally {
      setLoading(false);
    }
  }, [locationFilter, statusFilter, fromFilter, toFilter, page]);

  useEffect(() => {
    loadBookings();
  }, [loadBookings]);

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div>
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.allBookings')}</h1>
      <p className="mt-1 text-[13.5px] text-ink-400">
        <span className="font-medium text-ink-700 num">{totalCount}</span> booking{totalCount !== 1 ? 's' : ''} match your filters.
      </p>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">{error}</div>
      )}

      {/* Filters */}
      <div className="mt-5 flex flex-wrap items-end gap-3">
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">Location</label>
          <select
            value={locationFilter}
            onChange={(e) => {
              setLocationFilter(e.target.value);
              setPage(1);
            }}
            className="rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900"
          >
            <option value="">All locations</option>
            {locations.map((l) => (
              <option key={l.id} value={l.id}>{l.name}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">Status</label>
          <select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value);
              setPage(1);
            }}
            className="rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900"
          >
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">From</label>
          <input
            type="date"
            value={fromFilter}
            onChange={(e) => {
              setFromFilter(e.target.value);
              setPage(1);
            }}
            className="rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900 num"
          />
        </div>
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">To</label>
          <input
            type="date"
            value={toFilter}
            onChange={(e) => {
              setToFilter(e.target.value);
              setPage(1);
            }}
            className="rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900 num"
          />
        </div>
      </div>

      {/* Table */}
      <div className="mt-6 overflow-hidden rounded-card border border-line bg-white shadow-card">
        <table className="min-w-full num">
          <thead className="bg-surface-warm">
            <tr>
              <Th>Date</Th>
              <Th>Time</Th>
              <Th>Location</Th>
              <Th>User</Th>
              <Th>Slot</Th>
              <Th>Status</Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {loading ? (
              <tr>
                <td colSpan={6} className="px-4 py-10 text-center text-[13px] text-ink-400">Loading…</td>
              </tr>
            ) : bookings.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-4 py-10 text-center text-[13px] text-ink-400">No bookings match your filters.</td>
              </tr>
            ) : (
              bookings.map((b) => (
                <tr key={b.id} className="hover:bg-surface-warm/60">
                  <Td className="font-medium text-ink-900">{b.date}</Td>
                  <Td className="text-ink-500">{b.timeSlot}</Td>
                  <Td className="text-ink-700">{b.locationName}</Td>
                  <Td>
                    <div className="text-ink-900">{b.userDisplayName}</div>
                    <div className="text-[11.5px] text-ink-400">{b.userEmail}</div>
                  </Td>
                  <Td className="font-semibold text-ink-700">
                    {b.parkingSlotNumber ?? <span className="font-normal text-ink-300">—</span>}
                  </Td>
                  <Td>
                    <BookingStatusBadge status={b.status as BookingStatus} />
                  </Td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="mt-5 flex items-center justify-center gap-3">
          <button
            type="button"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            ← Previous
          </button>
          <span className="text-[12.5px] text-ink-400">
            Page <span className="font-semibold text-ink-700 num">{page}</span> of <span className="num">{totalPages}</span>
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            Next →
          </button>
        </div>
      )}
    </div>
  );
}

function Th({ children }: { children: React.ReactNode }) {
  return (
    <th className="px-4 py-3 text-left text-[11px] font-semibold uppercase tracking-[0.06em] text-ink-400 border-b border-line">
      {children}
    </th>
  );
}
function Td({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <td className={`px-4 py-3.5 text-[13.5px] text-ink-700 ${className}`}>{children}</td>;
}
