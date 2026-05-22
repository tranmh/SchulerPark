import { useEffect, useState, useMemo } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Trans, useTranslation } from 'react-i18next';
import { useAuth } from '../../contexts/AuthContext';
import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';
import { LocationSelector } from '../../components/LocationSelector';
import { CalendarPicker } from '../../components/CalendarPicker';
import { TimeSlotSelector } from '../../components/TimeSlotSelector';
import { ParkingGridView } from '../../components/grid/ParkingGridView';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import type { Location, Availability, Booking, TimeSlot, SkippedDay } from '../../types/booking';
import type { GridAvailability } from '../../types/grid';

function getWeekFriday(mondayStr: string): string {
  const [y, m, d] = mondayStr.split('-').map(Number);
  const fri = new Date(y, m - 1, d + 4);
  return `${fri.getFullYear()}-${String(fri.getMonth() + 1).padStart(2, '0')}-${String(fri.getDate()).padStart(2, '0')}`;
}

export function BookingPage() {
  const { i18n, t } = useTranslation();
  const navigate = useNavigate();
  const { user } = useAuth();
  const [searchParams] = useSearchParams();
  const preselectedLocation = searchParams.get('location');

  const locale = i18n.language.startsWith('de') ? 'de-DE' : 'en-GB';

  const formatDateShort = (dateStr: string) =>
    new Date(dateStr + 'T00:00:00').toLocaleDateString(locale, { weekday: 'short', day: 'numeric', month: 'short' });

  const formatDateLong = (dateStr: string) =>
    new Date(dateStr + 'T00:00:00').toLocaleDateString(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

  const STEPS = [t('booking.stepLocation'), t('booking.stepDate'), t('booking.stepTimeSlot'), t('booking.stepConfirm')];

  const [step, setStep] = useState(preselectedLocation ? 2 : 1);
  const [locationId, setLocationId] = useState<string | null>(preselectedLocation);
  const [locationFromPreference, setLocationFromPreference] = useState(false);
  const [date, setDate] = useState<string | null>(null);
  const [timeSlot, setTimeSlot] = useState<TimeSlot | null>(null);
  const [weekMode, setWeekMode] = useState(false);

  const [locations, setLocations] = useState<Location[]>([]);
  const [availability, setAvailability] = useState<Availability[]>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [gridAvailability, setGridAvailability] = useState<GridAvailability | null>(null);

  const [weekResult, setWeekResult] = useState<{
    created: Booking[];
    skipped: SkippedDay[];
  } | null>(null);

  const [singleResult, setSingleResult] = useState<Booking | null>(null);

  useEffect(() => {
    locationService.getLocations()
      .then((locs) => {
        setLocations(locs);
        if (!preselectedLocation && user?.preferredLocationId) {
          const stillActive = locs.some((l) => l.id === user.preferredLocationId);
          if (stillActive) {
            setLocationId(user.preferredLocationId);
            setLocationFromPreference(true);
            setStep(2);
          }
        }
      })
      .catch(() => setError(t('booking.loadLocationsFailed')))
      .finally(() => setLoading(false));
  }, [preselectedLocation, user?.preferredLocationId, t]);

  useEffect(() => {
    if (!locationId) return;
    setAvailability([]);
    locationService.getAvailability(locationId)
      .then(setAvailability)
      .catch(() => setError(t('booking.loadAvailabilityFailed')));
  }, [locationId, t]);

  const selectedLocation = locations.find((l) => l.id === locationId);

  const blockedDates = useMemo(() => {
    const dateMap = new Map<string, { morning: number; afternoon: number }>();
    for (const a of availability) {
      const entry = dateMap.get(a.date) ?? { morning: 0, afternoon: 0 };
      if (a.timeSlot === 'Morning') entry.morning = a.availableSlots;
      else entry.afternoon = a.availableSlots;
      dateMap.set(a.date, entry);
    }
    const blocked = new Set<string>();
    for (const [d, slots] of dateMap) {
      if (slots.morning <= 0 && slots.afternoon <= 0) blocked.add(d);
    }
    return blocked;
  }, [availability]);

  const availabilityMap = useMemo(() => {
    const map = new Map<string, { morning: number; afternoon: number }>();
    for (const a of availability) {
      const entry = map.get(a.date) ?? { morning: 0, afternoon: 0 };
      if (a.timeSlot === 'Morning') entry.morning = a.availableSlots;
      else entry.afternoon = a.availableSlots;
      map.set(a.date, entry);
    }
    return map;
  }, [availability]);

  const dateAvailability = date ? availabilityMap.get(date) : undefined;

  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  const minDate = tomorrow.toISOString().split('T')[0];
  const maxDate = new Date(tomorrow.getTime() + 30 * 24 * 60 * 60 * 1000).toISOString().split('T')[0];

  const handleLocationSelect = (id: string) => {
    setLocationId(id);
    setLocationFromPreference(false);
    setDate(null);
    setTimeSlot(null);
    setStep(2);
    setError(null);
    setWeekResult(null);
  };

  const handleDateSelect = (d: string) => {
    setDate(d);
    setTimeSlot(null);
    setStep(3);
    setError(null);
  };

  const handleTimeSlotSelect = (ts: TimeSlot) => {
    setTimeSlot(ts);
    setStep(4);
    setError(null);
    setGridAvailability(null);
    if (locationId && date && !weekMode) {
      locationService.getGridAvailability(locationId, date, ts)
        .then((ga) => {
          if (ga.gridRows > 0) setGridAvailability(ga);
        })
        .catch(() => {});
    }
  };

  const handleSubmit = async () => {
    if (!locationId || !date || !timeSlot) return;
    setIsSubmitting(true);
    setError(null);
    const submittedLocationId = locationFromPreference ? null : locationId;

    try {
      if (weekMode) {
        const result = await bookingService.createWeek({
          locationId: submittedLocationId,
          weekStartDate: date,
          timeSlot,
        });
        const hasFallback = result.createdBookings.some((b) => b.fallbackReason);
        if (result.skippedDays.length > 0 || hasFallback) {
          setWeekResult({ created: result.createdBookings, skipped: result.skippedDays });
        } else {
          navigate('/my-bookings');
        }
      } else {
        const booking = await bookingService.create({ locationId: submittedLocationId, date, timeSlot });
        if (booking.fallbackReason) {
          setSingleResult(booking);
        } else {
          navigate('/my-bookings');
        }
      }
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail
        ?? t('booking.createFailed');
      setError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) return <LoadingSpinner />;

  /* -------- Single-booking fallback summary ------------------------- */
  if (singleResult) {
    return (
      <div>
        <PageHeader title={t('booking.singleResultTitle')} subtitle={t('booking.singleResultSubtitle')} />
        <div className="mt-6 max-w-2xl rounded-card border border-line bg-white p-6 shadow-card">
          <div className="mb-5 flex items-start gap-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-[13px] text-amber-800">
            <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            <span>{singleResult.fallbackReason}</span>
          </div>
          <SummaryRow label={t('booking.labelLocation')} value={singleResult.locationName} />
          <SummaryRow label={t('booking.labelDate')} value={formatDateShort(singleResult.date)} />
          <SummaryRow label={t('booking.labelTimeSlot')} value={t(`components.timeSlot.${singleResult.timeSlot}`)} />
          <button
            type="button"
            onClick={() => navigate('/my-bookings')}
            className="mt-6 rounded-lg bg-brand-500 px-5 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600"
          >
            {t('booking.goToMyBookings')}
          </button>
        </div>
      </div>
    );
  }

  /* -------- Week booking result summary ----------------------------- */
  if (weekResult) {
    const fallbackBookings = weekResult.created.filter((b) => b.fallbackReason);
    return (
      <div>
        <PageHeader title={t('booking.weekResultTitle')} subtitle={t('booking.weekResultSubtitle')} />
        <div className="mt-6 max-w-2xl rounded-card border border-line bg-white p-6 shadow-card">
          <div className="mb-5 flex items-start gap-3 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-[13px] text-emerald-800">
            <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
            </svg>
            <span>
              <Trans
                i18nKey="booking.weekCreated"
                count={weekResult.created.length}
                values={{ count: weekResult.created.length }}
                components={{ strong: <span className="font-semibold" /> }}
              />
            </span>
          </div>

          {fallbackBookings.length > 0 && (
            <div className="mb-5">
              <h3 className="mb-2 text-[12.5px] font-semibold text-ink-700">{t('booking.fallbackHeading')}</h3>
              <ul className="space-y-1.5">
                {fallbackBookings.map((b) => (
                  <li key={b.id} className="flex items-start gap-2 text-[13px] text-amber-800">
                    <span className="mt-1.5 inline-block h-1.5 w-1.5 shrink-0 rounded-full bg-amber-500" />
                    <span>
                      {formatDateShort(b.date)} → <strong>{b.locationName}</strong>. {b.fallbackReason}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {weekResult.skipped.length > 0 && (
            <div>
              <h3 className="mb-2 text-[12.5px] font-semibold text-ink-700">{t('booking.skippedHeading')}</h3>
              <ul className="space-y-1.5">
                {weekResult.skipped.map((s) => (
                  <li key={s.date} className="flex items-center gap-2 text-[13px] text-amber-700">
                    <span className="inline-block h-1.5 w-1.5 rounded-full bg-amber-500" />
                    {formatDateShort(s.date)} — {s.reason}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <button
            type="button"
            onClick={() => navigate('/my-bookings')}
            className="mt-6 rounded-lg bg-brand-500 px-5 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600"
          >
            {t('booking.goToMyBookings')}
          </button>
        </div>
      </div>
    );
  }

  /* -------- Main flow ----------------------------------------------- */
  return (
    <div>
      <PageHeader
        title={t('booking.pageTitle')}
        subtitle={
          selectedLocation
            ? (weekMode
                ? t('booking.pickWeekSubtitle', { location: selectedLocation.name })
                : t('booking.pickDateSubtitle', { location: selectedLocation.name }))
            : t('booking.selectLocationSubtitle')
        }
      />

      {/* Stepper */}
      <div className="mt-6 flex flex-wrap items-center gap-2">
        {STEPS.map((label, i) => {
          const stepNum = i + 1;
          const isCurrent = step === stepNum;
          const isDone = step > stepNum;

          let cls = 'inline-flex items-center gap-2 rounded-full pr-3.5 py-1 pl-1 text-[12.5px] font-medium transition-colors';
          let circleCls = 'grid h-5 w-5 place-items-center rounded-full text-[10.5px] font-semibold';

          if (isCurrent) {
            cls += ' bg-brand-100 text-brand-800';
            circleCls += ' bg-brand-500 text-white';
          } else if (isDone) {
            cls += ' bg-emerald-100 text-emerald-800 hover:bg-emerald-200 cursor-pointer';
            circleCls += ' bg-emerald-500 text-white';
          } else {
            cls += ' bg-line/60 text-ink-300';
            circleCls += ' bg-line-strong text-white';
          }

          return (
            <div key={label} className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => {
                  if (isDone) setStep(stepNum);
                }}
                disabled={!isDone}
                className={cls}
              >
                <span className={circleCls}>{isDone ? '✓' : stepNum}</span>
                {label}
              </button>
              {i < STEPS.length - 1 && (
                <svg className="h-3.5 w-3.5 text-ink-300" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 5l7 7-7 7" />
                </svg>
              )}
            </div>
          );
        })}
      </div>

      {error && (
        <div className="mt-5 flex items-start gap-2.5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
          <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
          </svg>
          <span>{error}</span>
        </div>
      )}

      <div className="mt-7">
        {/* Step 1: Location */}
        {step === 1 && (
          <div>
            <h2 className="mb-4 text-[15px] font-semibold text-ink-700">{t('booking.selectLocation')}</h2>
            <LocationSelector locations={locations} selectedId={locationId} onChange={handleLocationSelect} />
          </div>
        )}

        {/* Step 2: Date */}
        {step === 2 && (
          <div>
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-[15px] font-semibold text-ink-700">
                {weekMode
                  ? t('booking.selectWeekAt', { location: selectedLocation?.name ?? '' })
                  : t('booking.selectDateAt', { location: selectedLocation?.name ?? '' })}
              </h2>
              <label className="inline-flex cursor-pointer items-center gap-2.5 rounded-lg border border-line bg-white px-3 py-1.5 text-[12.5px] text-ink-700">
                <span className="relative inline-flex h-5 w-9 items-center">
                  <input
                    type="checkbox"
                    checked={weekMode}
                    onChange={(e) => {
                      setWeekMode(e.target.checked);
                      setDate(null);
                    }}
                    className="peer sr-only"
                  />
                  <span className="absolute inset-0 rounded-full bg-line-strong peer-checked:bg-brand-500 transition-colors" />
                  <span className="absolute top-0.5 left-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform peer-checked:translate-x-4" />
                </span>
                {t('booking.bookEntireWeek')}
              </label>
            </div>
            <div className="grid gap-7 lg:grid-cols-[420px_1fr]">
              <CalendarPicker
                selectedDate={date}
                onSelect={handleDateSelect}
                blockedDates={blockedDates}
                availability={availabilityMap}
                minDate={minDate}
                maxDate={maxDate}
                weekMode={weekMode}
              />
              <div className="space-y-4">
                <div className="rounded-card border border-line bg-brand-50/40 p-5">
                  <div className="flex items-start gap-3">
                    <div className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-brand-500 text-white">
                      <svg className="h-[18px] w-[18px]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                    </div>
                    <div className="text-[12.5px] leading-relaxed text-brand-900">
                      <Trans i18nKey="booking.lotteryInfo" components={{ b: <b /> }} />
                    </div>
                  </div>
                </div>
                <div className="rounded-card border border-line bg-white p-5 text-[12.5px] text-ink-500">
                  <div className="font-semibold text-ink-700">{t('booking.tip')}</div>
                  <Trans
                    i18nKey="booking.calendarLegend"
                    components={{
                      green: <span className="text-emerald-700" />,
                      amber: <span className="text-amber-700" />,
                      rose: <span className="text-rose-700" />,
                    }}
                  />
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Step 3: Time Slot */}
        {step === 3 && (
          <div>
            <h2 className="mb-4 text-[15px] font-semibold text-ink-700">
              {weekMode && date
                ? t('booking.selectTimeSlotForWeek', { start: formatDateShort(date), end: formatDateShort(getWeekFriday(date)) })
                : date && t('booking.selectTimeSlotFor', { date: formatDateLong(date) })}
            </h2>
            <TimeSlotSelector
              value={timeSlot}
              onChange={handleTimeSlotSelect}
              morningAvailable={dateAvailability?.morning}
              afternoonAvailable={dateAvailability?.afternoon}
            />
          </div>
        )}

        {/* Step 4: Review & Submit */}
        {step === 4 && (
          <div>
            <h2 className="mb-4 text-[15px] font-semibold text-ink-700">{t('booking.reviewTitle')}</h2>
            <div className="grid gap-5 lg:grid-cols-[1fr_320px]">
              <div className="rounded-card border border-line bg-white p-6 shadow-card">
                <SummaryRow label={t('booking.labelLocation')} value={selectedLocation?.name ?? '—'} />
                <SummaryRow
                  label={weekMode ? t('booking.labelWeek') : t('booking.labelDate')}
                  value={
                    weekMode && date
                      ? `${formatDateShort(date)} – ${formatDateShort(getWeekFriday(date))}`
                      : date
                        ? formatDateLong(date)
                        : '—'
                  }
                />
                <SummaryRow label={t('booking.labelTimeSlot')} value={timeSlot ? t(`components.timeSlot.${timeSlot}`) : '—'} />
                {weekMode && (
                  <div className="mt-4 flex items-start gap-2 rounded-lg bg-brand-50 px-3 py-2.5 text-[12px] text-brand-800">
                    <svg className="mt-0.5 h-3.5 w-3.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    {t('booking.weekInfo')}
                  </div>
                )}

                {gridAvailability && !weekMode && (
                  <div className="mt-6">
                    <h3 className="mb-2 text-[12.5px] font-semibold text-ink-700">{t('booking.parkingLayout')}</h3>
                    <ParkingGridView availability={gridAvailability} />
                  </div>
                )}

                <div className="mt-6 flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={() => setStep(1)}
                    className="rounded-lg border border-line-strong bg-white px-4 py-2 text-[13px] font-medium text-ink-700 hover:bg-surface-sunken"
                  >
                    {t('booking.startOver')}
                  </button>
                  <button
                    type="button"
                    onClick={handleSubmit}
                    disabled={isSubmitting}
                    className="rounded-lg bg-brand-500 px-5 py-2 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-60"
                  >
                    {isSubmitting ? t('booking.submitting') : weekMode ? t('booking.bookingWeek') : t('booking.confirmBooking')}
                  </button>
                </div>
              </div>
              <aside className="rounded-card border border-line bg-surface-warm p-5 text-[12.5px] leading-relaxed text-ink-500">
                <div className="font-semibold text-ink-900 text-[13px]">{t('booking.whatHappensNext')}</div>
                <ol className="mt-3 space-y-2.5">
                  <li className="flex gap-2.5"><span className="num font-semibold text-brand-600">1.</span>{t('booking.next1')}</li>
                  <li className="flex gap-2.5"><span className="num font-semibold text-brand-600">2.</span><Trans i18nKey="booking.next2" components={{ b: <b /> }} /></li>
                  <li className="flex gap-2.5"><span className="num font-semibold text-brand-600">3.</span><Trans i18nKey="booking.next3" components={{ b: <b /> }} /></li>
                </ol>
              </aside>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function PageHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <div>
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{title}</h1>
      {subtitle && <p className="mt-1 text-[13.5px] text-ink-400">{subtitle}</p>}
    </div>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between border-b border-line py-3 last:border-b-0">
      <div className="text-[12.5px] text-ink-400">{label}</div>
      <div className="text-[13.5px] font-medium text-ink-900">{value}</div>
    </div>
  );
}
