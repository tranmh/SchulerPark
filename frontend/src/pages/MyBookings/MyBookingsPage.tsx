import { useEffect, useState, useCallback, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { bookingService } from '../../services/bookingService';
import { BookingStatusBadge } from '../../components/BookingStatusBadge';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import type { Booking, BookingStatus } from '../../types/booking';

const STATUS_OPTIONS: (BookingStatus | 'All')[] = ['All', 'Pending', 'Won', 'Lost', 'Confirmed', 'Cancelled', 'Expired'];

export function MyBookingsPage() {
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState<BookingStatus | 'All'>('All');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Cancel dialog
  const [cancelTarget, setCancelTarget] = useState<Booking | null>(null);
  const [isCancelling, setIsCancelling] = useState(false);

  // Action loading
  const [confirmingId, setConfirmingId] = useState<string | null>(null);

  const pageSize = 20;

  const loadBookings = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await bookingService.getMyBookings({
        page,
        pageSize,
        status: statusFilter === 'All' ? undefined : statusFilter,
      });
      setBookings(res.bookings);
      setTotalCount(res.totalCount);
    } catch {
      setError('Failed to load bookings.');
    } finally {
      setLoading(false);
    }
  }, [page, statusFilter]);

  useEffect(() => { loadBookings(); }, [loadBookings]);

  const handleCancel = async () => {
    if (!cancelTarget) return;
    setIsCancelling(true);
    try {
      await bookingService.cancel(cancelTarget.id);
      setCancelTarget(null);
      await loadBookings();
    } catch {
      setError('Failed to cancel booking.');
    } finally {
      setIsCancelling(false);
    }
  };

  const handleConfirm = async (bookingId: string) => {
    setConfirmingId(bookingId);
    setError(null);
    try {
      const updated = await bookingService.confirm(bookingId);
      setBookings((prev) => prev.map((b) => (b.id === bookingId ? updated : b)));
    } catch {
      setError('Failed to confirm booking.');
    } finally {
      setConfirmingId(null);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div>
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">My Bookings</h1>
          <p className="mt-1 text-sm text-gray-500">{totalCount} booking{totalCount !== 1 ? 's' : ''} total</p>
        </div>
        <Link
          to="/booking"
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
        >
          New Booking
        </Link>
      </div>

      {/* Filters */}
      <div className="mt-6 flex gap-2">
        {STATUS_OPTIONS.map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => { setStatusFilter(s); setPage(1); }}
            className={`rounded-full px-3 py-1 text-sm font-medium transition-colors ${
              statusFilter === s
                ? 'bg-blue-100 text-blue-800'
                : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
            }`}
          >
            {s}
          </button>
        ))}
      </div>

      {error && (
        <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>
      )}

      {/* Bookings list */}
      {loading ? (
        <div className="mt-8 flex justify-center text-gray-400">Loading...</div>
      ) : bookings.length === 0 ? (
        <div className="mt-8 rounded-lg border-2 border-dashed border-gray-200 p-8 text-center">
          <p className="text-gray-500">No bookings found.</p>
          <Link to="/booking" className="mt-2 inline-block text-sm font-medium text-blue-600 hover:text-blue-800">
            Book a parking spot
          </Link>
        </div>
      ) : (
        <div className="mt-6 space-y-3">
          {bookings.map((b) => (
            <div key={b.id} className="rounded-lg border border-gray-200 bg-white p-4">
              <div className="flex items-start justify-between">
                <div>
                  <div className="font-medium text-gray-900">
                    {new Date(b.date + 'T00:00:00').toLocaleDateString('de-DE', {
                      weekday: 'short', day: 'numeric', month: 'short', year: 'numeric',
                    })}
                    {' '}&middot;{' '}{b.timeSlot}
                  </div>
                  <div className="mt-1 text-sm text-gray-500">{b.locationName}</div>
                  {b.parkingSlotNumber && (
                    <div className="mt-0.5 text-sm text-gray-500">Assigned slot: <span className="font-medium">{b.parkingSlotNumber}</span></div>
                  )}
                </div>
                <BookingStatusBadge status={b.status} />
              </div>

              {/* Actions */}
              <div className="mt-3 flex gap-2 items-center">
                {b.status === 'Won' && (
                  <>
                    <button
                      type="button"
                      onClick={() => handleConfirm(b.id)}
                      disabled={confirmingId === b.id}
                      className="rounded-md bg-green-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
                    >
                      {confirmingId === b.id ? 'Confirming...' : 'Confirm Usage'}
                    </button>
                    {b.confirmationDeadline && <DeadlineCountdown deadline={b.confirmationDeadline} />}
                  </>
                )}
                {(b.status === 'Pending' || b.status === 'Won') && (
                  <button
                    type="button"
                    onClick={() => setCancelTarget(b)}
                    className="rounded-md border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="mt-6 flex items-center justify-center gap-2">
          <button
            type="button"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Previous
          </button>
          <span className="text-sm text-gray-600">
            Page {page} of {totalPages}
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Next
          </button>
        </div>
      )}

      {/* Cancel confirmation dialog */}
      <ConfirmDialog
        isOpen={!!cancelTarget}
        title="Cancel Booking"
        message={cancelTarget
          ? `Cancel your ${cancelTarget.timeSlot} booking at ${cancelTarget.locationName} on ${cancelTarget.date}?`
          : ''}
        confirmLabel="Cancel Booking"
        cancelLabel="Keep It"
        onConfirm={handleCancel}
        onCancel={() => setCancelTarget(null)}
        isLoading={isCancelling}
      />
    </div>
  );
}

function DeadlineCountdown({ deadline }: { deadline: string }) {
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(timer);
  }, []);

  const remaining = useMemo(() => {
    const deadlineMs = new Date(deadline).getTime();
    const diff = deadlineMs - now;
    if (diff <= 0) return null;

    const hours = Math.floor(diff / 3_600_000);
    const minutes = Math.floor((diff % 3_600_000) / 60_000);
    if (hours > 0) return `${hours}h ${minutes}m remaining`;
    return `${minutes}m remaining`;
  }, [deadline, now]);

  if (!remaining) {
    return <span className="text-xs text-red-500 font-medium">Deadline passed</span>;
  }

  return (
    <span className="text-xs text-amber-600 font-medium">
      {remaining}
    </span>
  );
}
