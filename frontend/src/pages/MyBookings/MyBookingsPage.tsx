import { useEffect, useState, useCallback, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Trans, useTranslation } from 'react-i18next';
import { bookingService } from '../../services/bookingService';
import { BookingStatusBadge } from '../../components/BookingStatusBadge';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import type { Booking, BookingStatus } from '../../types/booking';

const STATUS_OPTIONS: (BookingStatus | 'All')[] = [
  'All',
  'Pending',
  'Won',
  'Lost',
  'Confirmed',
  'Cancelled',
  'Expired',
];

export function MyBookingsPage() {
  const { i18n, t } = useTranslation();
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState<BookingStatus | 'All'>('All');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [cancelTarget, setCancelTarget] = useState<Booking | null>(null);
  const [isCancelling, setIsCancelling] = useState(false);

  const [confirmingId, setConfirmingId] = useState<string | null>(null);

  const pageSize = 20;
  const locale = i18n.language.startsWith('de') ? 'de-DE' : 'en-US';

  const formatDayParts = (dateStr: string) => {
    const d = new Date(dateStr + 'T00:00:00');
    return {
      weekday: d.toLocaleDateString(locale, { weekday: 'short' }),
      day: d.getDate(),
      month: d.toLocaleDateString(locale, { month: 'short' }),
    };
  };

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
      setError(t('myBookings.loadFailed'));
    } finally {
      setLoading(false);
    }
  }, [page, statusFilter, t]);

  useEffect(() => {
    loadBookings();
  }, [loadBookings]);

  const handleCancel = async () => {
    if (!cancelTarget) return;
    setIsCancelling(true);
    try {
      await bookingService.cancel(cancelTarget.id);
      setCancelTarget(null);
      await loadBookings();
    } catch {
      setError(t('myBookings.cancelFailed'));
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
      setError(t('myBookings.confirmFailed'));
    } finally {
      setConfirmingId(null);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);
  const activeCount = useMemo(
    () => bookings.filter((b) => b.status === 'Pending' || b.status === 'Won' || b.status === 'Confirmed').length,
    [bookings]
  );

  const filterLabel = (s: BookingStatus | 'All') =>
    s === 'All' ? t('common.all') : t(`components.status.${s}`);

  return (
    <div>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('myBookings.title')}</h1>
          <p className="mt-1 text-[13.5px] text-ink-400">
            <Trans
              i18nKey="myBookings.summaryTotal"
              count={totalCount}
              values={{ count: totalCount }}
              components={{ strong: <span className="font-medium text-ink-700 num" /> }}
            />
            {activeCount > 0 && (
              <>
                {' · '}
                <Trans
                  i18nKey="myBookings.activeSuffix"
                  values={{ count: activeCount }}
                  components={{ strong: <span className="font-medium text-ink-700 num" /> }}
                />
              </>
            )}
          </p>
        </div>
        <Link
          to="/booking"
          className="inline-flex items-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600"
        >
          <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          {t('myBookings.newBooking')}
        </Link>
      </div>

      {/* Filter chips */}
      <div className="mt-6 inline-flex flex-wrap items-center gap-1 rounded-lg border border-line bg-white p-1">
        {STATUS_OPTIONS.map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => {
              setStatusFilter(s);
              setPage(1);
            }}
            className={`rounded-md px-3 py-1.5 text-[12.5px] font-medium transition-colors ${
              statusFilter === s
                ? 'bg-brand-500 text-white shadow-sm'
                : 'text-ink-500 hover:bg-line/60 hover:text-ink-900'
            }`}
          >
            {filterLabel(s)}
          </button>
        ))}
      </div>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
          {error}
        </div>
      )}

      {/* Bookings list */}
      {loading ? (
        <LoadingSpinner />
      ) : bookings.length === 0 ? (
        <div className="mt-6 flex flex-col items-center gap-2 rounded-card border border-dashed border-line bg-white px-6 py-12 text-center">
          <div className="grid h-11 w-11 place-items-center rounded-full bg-brand-50 text-brand-600">
            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
            </svg>
          </div>
          <div className="mt-1 text-[14px] font-semibold text-ink-900">{t('myBookings.emptyTitle')}</div>
          <div className="text-[12.5px] text-ink-400">{t('myBookings.emptyDescription')}</div>
          <Link
            to="/booking"
            className="mt-3 inline-flex items-center gap-1.5 text-[13px] font-medium text-brand-500 hover:text-brand-700"
          >
            {t('myBookings.emptyCta')}
          </Link>
        </div>
      ) : (
        <div className="mt-6 divide-y divide-line overflow-hidden rounded-card border border-line bg-white shadow-card">
          {bookings.map((b) => {
            const { weekday, day, month } = formatDayParts(b.date);
            const isFaded = b.status === 'Cancelled' || b.status === 'Expired' || b.status === 'Lost';
            return (
              <div key={b.id} className={`flex flex-wrap items-center gap-4 p-5 ${isFaded ? 'opacity-90' : ''}`}>
                <div className="w-14 text-center">
                  <div className={`text-[10.5px] font-semibold uppercase tracking-wider ${isFaded ? 'text-ink-300' : 'text-brand-500'}`}>
                    {weekday}
                  </div>
                  <div className={`mt-0.5 text-[22px] font-bold leading-none num ${isFaded ? 'text-ink-300' : 'text-ink-900'}`}>
                    {day}
                  </div>
                  <div className="mt-1 text-[10.5px] text-ink-400">{month}</div>
                </div>
                <div className="h-12 w-px bg-line" />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <div className={`text-[14.5px] font-semibold ${isFaded ? 'text-ink-500 line-through' : 'text-ink-900'}`}>
                      {b.locationName} · {t(`components.timeSlot.${b.timeSlot}`)}
                    </div>
                    <BookingStatusBadge status={b.status} />
                  </div>
                  <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[12.5px] text-ink-400">
                    <span>{b.timeSlot === 'Morning' ? t('components.timeSlot.morningRange') : t('components.timeSlot.afternoonRange')}</span>
                    {b.parkingSlotNumber && (
                      <>
                        <span>·</span>
                        <span className="font-semibold text-ink-700 num">{t('myBookings.slot', { n: b.parkingSlotNumber })}</span>
                      </>
                    )}
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  {b.status === 'Won' && (
                    <>
                      {b.confirmationDeadline && <DeadlineCountdown deadline={b.confirmationDeadline} />}
                      <button
                        type="button"
                        onClick={() => handleConfirm(b.id)}
                        disabled={confirmingId === b.id}
                        className="rounded-lg bg-emerald-600 px-3.5 py-2 text-[12.5px] font-medium text-white shadow-sm transition-colors hover:bg-emerald-700 disabled:opacity-60"
                      >
                        {confirmingId === b.id ? t('myBookings.confirming') : t('myBookings.confirmUsage')}
                      </button>
                    </>
                  )}
                  {(b.status === 'Pending' || b.status === 'Won' || b.status === 'Confirmed') && (
                    <button
                      type="button"
                      onClick={() => setCancelTarget(b)}
                      className="rounded-lg border border-line-strong bg-white px-3 py-2 text-[12.5px] font-medium text-ink-700 hover:bg-surface-sunken"
                    >
                      {t('myBookings.cancel')}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="mt-6 flex items-center justify-center gap-3">
          <button
            type="button"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            ← {t('common.previous')}
          </button>
          <span className="text-[12.5px] text-ink-400">
            {t('common.page')} <span className="font-semibold text-ink-700 num">{page}</span> {t('common.of')} <span className="num">{totalPages}</span>
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            {t('common.next')} →
          </button>
        </div>
      )}

      {/* Cancel confirmation dialog */}
      <ConfirmDialog
        isOpen={!!cancelTarget}
        title={t('myBookings.cancelTitle')}
        message={
          cancelTarget
            ? t('myBookings.cancelMessage', {
                timeSlot: t(`components.timeSlot.${cancelTarget.timeSlot}`),
                location: cancelTarget.locationName,
                date: cancelTarget.date,
              })
            : ''
        }
        confirmLabel={t('myBookings.cancelConfirm')}
        cancelLabel={t('myBookings.cancelKeep')}
        onConfirm={handleCancel}
        onCancel={() => setCancelTarget(null)}
        isLoading={isCancelling}
      />
    </div>
  );
}

function DeadlineCountdown({ deadline }: { deadline: string }) {
  const { t } = useTranslation();
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
    if (hours > 0) return t('myBookings.hoursLeft', { h: hours, m: minutes });
    return t('myBookings.minutesLeft', { m: minutes });
  }, [deadline, now, t]);

  if (!remaining) {
    return (
      <span className="rounded-md bg-rose-50 px-2 py-1 text-[11.5px] font-medium text-rose-700 ring-1 ring-inset ring-rose-200">
        {t('myBookings.deadlinePassed')}
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-1.5 rounded-md bg-amber-50 px-2 py-1 text-[11.5px] font-medium text-amber-700 ring-1 ring-inset ring-amber-200 num">
      <svg className="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      {remaining}
    </span>
  );
}
