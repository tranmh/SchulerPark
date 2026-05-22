import { useEffect, useState, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Trans, useTranslation } from 'react-i18next';
import { useAuth } from '../../contexts/AuthContext';
import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';
import { BookingStatusBadge } from '../../components/BookingStatusBadge';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import type { Booking, Location } from '../../types/booking';

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('');
}

export function DashboardPage() {
  const { i18n, t } = useTranslation();
  const { user } = useAuth();
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [locations, setLocations] = useState<Location[]>([]);
  const [loading, setLoading] = useState(true);

  const locale = i18n.language.startsWith('de') ? 'de-DE' : 'en-GB';

  const formatDayParts = (dateStr: string) => {
    const d = new Date(dateStr + 'T00:00:00');
    return {
      weekday: d.toLocaleDateString(locale, { weekday: 'short' }),
      day: d.getDate(),
      month: d.toLocaleDateString(locale, { month: 'short' }),
    };
  };

  const todayLabel = () =>
    new Date().toLocaleDateString(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

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

  const wonCount = useMemo(() => bookings.filter((b) => b.status === 'Won').length, [bookings]);
  const firstWon = useMemo(() => bookings.find((b) => b.status === 'Won'), [bookings]);
  const greeting = useMemo(() => {
    const h = new Date().getHours();
    if (h < 12) return t('dashboard.morning');
    if (h < 18) return t('dashboard.afternoon');
    return t('dashboard.evening');
  }, [t]);

  if (loading) return <LoadingSpinner />;

  return (
    <div>
      {/* Greeting */}
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <div className="inline-flex items-center gap-2 text-[11.5px] font-semibold uppercase tracking-wider text-ink-400">
            <span className="h-1.5 w-1.5 rounded-full bg-brand-500" />
            {t('dashboard.todayPrefix')} · {todayLabel()}
          </div>
          <h1 className="mt-1.5 text-[28px] font-bold tracking-tight text-ink-900">
            {greeting}, {user?.displayName?.split(' ')[0] ?? t('dashboard.there')}.
          </h1>
          <p className="mt-1 text-[14px] text-ink-400">
            {wonCount > 0 ? (
              <Trans
                i18nKey="dashboard.waitingConfirmation"
                count={wonCount}
                values={{ count: wonCount }}
                components={{ strong: <span className="font-semibold text-brand-600" /> }}
              />
            ) : bookings.length > 0 ? (
              <Trans
                i18nKey="dashboard.upcomingCount"
                count={bookings.length}
                values={{ count: bookings.length }}
                components={{ strong: <span className="font-semibold text-ink-700" /> }}
              />
            ) : (
              t('dashboard.noUpcomingHint')
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
          {t('dashboard.newBooking')}
        </Link>
      </div>

      {/* Stat strip */}
      <div className="mt-7 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label={t('dashboard.statActive')} value={bookings.length.toString()} hint={t('dashboard.statActiveHint')} />
        <StatCard
          label={t('dashboard.statPreferredLocation')}
          value={(user?.preferredLocationId && locations.find((l) => l.id === user.preferredLocationId)?.name) || '—'}
          hint={user?.preferredSlotId ? t('dashboard.statPreferredSlot') : t('dashboard.statNoPreferredSlot')}
        />
        <StatCard
          label={t('dashboard.statLocationsAvailable')}
          value={locations.filter((l) => l.totalSlots > 0).length.toString()}
          hint={t('dashboard.statLocationsHint')}
        />
        {firstWon && firstWon.confirmationDeadline ? (
          <div className="rounded-card border border-transparent bg-gradient-to-br from-brand-500 to-brand-700 p-4 text-white shadow-card">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-brand-100">{t('dashboard.statConfirmBy')}</div>
            <div className="mt-1.5 text-[22px] font-bold tracking-tight num">
              {new Date(firstWon.confirmationDeadline).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}
            </div>
            <div className="mt-0.5 text-[12px] text-brand-100">
              {firstWon.locationName} · {t(`components.timeSlot.${firstWon.timeSlot}`)}
            </div>
          </div>
        ) : (
          <StatCard label={t('dashboard.statLotteryCutoff')} value="16:00" hint={t('dashboard.statLotteryHint')} />
        )}
      </div>

      {/* Upcoming Bookings */}
      <section className="mt-10">
        <div className="flex items-end justify-between">
          <div>
            <h2 className="text-[16px] font-semibold tracking-tight text-ink-900">{t('dashboard.upcomingTitle')}</h2>
            <p className="mt-0.5 text-[12.5px] text-ink-400">{t('dashboard.upcomingSubtitle')}</p>
          </div>
          <Link to="/my-bookings" className="text-[12.5px] font-medium text-brand-500 hover:text-brand-700">
            {t('dashboard.viewAll')}
          </Link>
        </div>

        {bookings.length === 0 ? (
          <EmptyState
            icon={
              <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
              </svg>
            }
            title={t('dashboard.emptyTitle')}
            description={t('dashboard.emptyDescription')}
            cta={
              <Link to="/booking" className="inline-flex items-center gap-1.5 text-[13px] font-medium text-brand-500 hover:text-brand-700">
                {t('dashboard.emptyCta')}
              </Link>
            }
          />
        ) : (
          <div className="mt-4 divide-y divide-line overflow-hidden rounded-card border border-line bg-white shadow-card">
            {bookings.map((b) => {
              const { weekday, day, month } = formatDayParts(b.date);
              return (
                <div key={b.id} className="flex items-center gap-4 p-4">
                  <div className="w-14 text-center">
                    <div className="text-[10.5px] font-semibold uppercase tracking-wider text-brand-500">{weekday}</div>
                    <div className="mt-0.5 text-[22px] font-bold leading-none text-ink-900 num">{day}</div>
                    <div className="mt-1 text-[10.5px] text-ink-400">{month}</div>
                  </div>
                  <div className="h-12 w-px bg-line" />
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <div className="truncate text-[14px] font-semibold text-ink-900">
                        {b.locationName} · {t(`components.timeSlot.${b.timeSlot}`)}
                      </div>
                      <BookingStatusBadge status={b.status} />
                    </div>
                    <div className="mt-1 truncate text-[12.5px] text-ink-400">
                      {b.timeSlot === 'Morning' ? t('components.timeSlot.morningRange') : t('components.timeSlot.afternoonRange')}
                      {b.parkingSlotNumber && (
                        <>
                          {' · '}
                          <span className="font-semibold text-ink-700 num">{t('myBookings.slot', { n: b.parkingSlotNumber })}</span>
                        </>
                      )}
                    </div>
                  </div>
                  {b.status === 'Won' && b.confirmationDeadline && (
                    <div className="hidden sm:block">
                      <div className="rounded-md bg-amber-50 px-2.5 py-1 text-[12px] font-medium text-amber-700 ring-1 ring-inset ring-amber-200 num">
                        {t('dashboard.confirmBy')}{' '}
                        {new Date(b.confirmationDeadline).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </section>

      {/* Quick Book */}
      <section className="mt-10">
        <div>
          <h2 className="text-[16px] font-semibold tracking-tight text-ink-900">{t('dashboard.quickBook')}</h2>
          <p className="mt-0.5 text-[12.5px] text-ink-400">{t('dashboard.quickBookSubtitle')}</p>
        </div>
        {locations.length === 0 ? (
          <EmptyState title={t('dashboard.noLocationsTitle')} description={t('dashboard.noLocationsDescription')} />
        ) : (
          <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {locations.map((loc) => (
              <Link
                key={loc.id}
                to={`/booking?location=${loc.id}`}
                className="group flex flex-col rounded-card border border-line bg-white p-5 shadow-card transition-all hover:border-line-strong hover:shadow-card-hover"
              >
                <div className="flex items-start justify-between">
                  <div className="grid h-10 w-10 place-items-center rounded-lg bg-brand-50 text-[13px] font-bold text-brand-700">
                    {initials(loc.name)}
                  </div>
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700 ring-1 ring-inset ring-emerald-200">
                    <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                    {t('common.active')}
                  </span>
                </div>
                <div className="mt-4 font-semibold text-ink-900">{loc.name}</div>
                <div className="mt-0.5 truncate text-[12.5px] text-ink-400">{loc.address}</div>
                <div className="mt-4 flex items-center justify-between border-t border-line pt-4">
                  <div className="text-[12px] text-ink-400 num">
                    {t('components.location.slots', { count: loc.totalSlots })}
                  </div>
                  <span className="text-[12.5px] font-medium text-brand-500 transition-transform group-hover:translate-x-0.5">
                    {t('dashboard.book')}
                  </span>
                </div>
              </Link>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function StatCard({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-card border border-line bg-white p-4 shadow-card">
      <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-400">{label}</div>
      <div className="mt-1.5 truncate text-[22px] font-bold tracking-tight text-ink-900 num">{value}</div>
      {hint && <div className="mt-0.5 text-[12px] text-ink-400">{hint}</div>}
    </div>
  );
}

function EmptyState({
  icon,
  title,
  description,
  cta,
}: {
  icon?: React.ReactNode;
  title: string;
  description?: string;
  cta?: React.ReactNode;
}) {
  return (
    <div className="mt-4 flex flex-col items-center gap-2 rounded-card border border-dashed border-line bg-white px-6 py-10 text-center">
      {icon && (
        <div className="grid h-11 w-11 place-items-center rounded-full bg-brand-50 text-brand-600">{icon}</div>
      )}
      <div className="mt-1 text-[14px] font-semibold text-ink-900">{title}</div>
      {description && <div className="max-w-sm text-[12.5px] text-ink-400">{description}</div>}
      {cta && <div className="mt-2">{cta}</div>}
    </div>
  );
}
