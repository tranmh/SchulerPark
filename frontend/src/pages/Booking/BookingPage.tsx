import { useEffect, useState, useMemo } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../../contexts/AuthContext';
import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';
import { LocationSelector } from '../../components/LocationSelector';
import { CalendarPicker } from '../../components/CalendarPicker';
import { TimeSlotSelector } from '../../components/TimeSlotSelector';
import { ParkingGridView } from '../../components/grid/ParkingGridView';
import type { Location, Availability, Booking, TimeSlot, SkippedDay } from '../../types/booking';
import type { GridAvailability } from '../../types/grid';

const STEPS = ['Location', 'Date', 'Time Slot', 'Confirm'];

function getWeekFriday(mondayStr: string): string {
  const [y, m, d] = mondayStr.split('-').map(Number);
  const fri = new Date(y, m - 1, d + 4);
  return `${fri.getFullYear()}-${String(fri.getMonth() + 1).padStart(2, '0')}-${String(fri.getDate()).padStart(2, '0')}`;
}

function formatDateDE(dateStr: string): string {
  return new Date(dateStr + 'T00:00:00').toLocaleDateString('de-DE', {
    weekday: 'short', day: 'numeric', month: 'short',
  });
}

export function BookingPage() {
  const navigate = useNavigate();
  const { user } = useAuth();
  const [searchParams] = useSearchParams();
  const preselectedLocation = searchParams.get('location');

  const [step, setStep] = useState(preselectedLocation ? 2 : 1);
  const [locationId, setLocationId] = useState<string | null>(preselectedLocation);
  // True when the current location came from the user's preference (not manual/URL).
  // Used to send locationId: null to the API so the backend resolver can fall back.
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

  // Week booking result
  const [weekResult, setWeekResult] = useState<{
    created: Booking[];
    skipped: SkippedDay[];
  } | null>(null);

  // Single booking fallback summary (only shown when the server reports a fallback)
  const [singleResult, setSingleResult] = useState<Booking | null>(null);

  // Load locations — then preselect preferred if no URL-provided location
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
      .catch(() => setError('Failed to load locations.'))
      .finally(() => setLoading(false));
  }, [preselectedLocation, user?.preferredLocationId]);

  // Load availability when location changes
  useEffect(() => {
    if (!locationId) return;
    setAvailability([]);
    locationService.getAvailability(locationId)
      .then(setAvailability)
      .catch(() => setError('Failed to load availability.'));
  }, [locationId]);

  const selectedLocation = locations.find((l) => l.id === locationId);

  // Compute blocked dates (location-wide: where both morning and afternoon are 0 available)
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

  // Availability map for calendar dots
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

  // Availability for selected date
  const dateAvailability = date ? availabilityMap.get(date) : undefined;

  // Min/max dates
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
        .then((ga) => { if (ga.gridRows > 0) setGridAvailability(ga); })
        .catch(() => {});
    }
  };

  const handleSubmit = async () => {
    if (!locationId || !date || !timeSlot) return;
    setIsSubmitting(true);
    setError(null);
    // When the location came from the user's preference, send null so the API
    // can resolve + fall back. When the user picked a location explicitly
    // (step 1 or URL param), honor that exact choice without fallback.
    const submittedLocationId = locationFromPreference ? null : locationId;

    try {
      if (weekMode) {
        const result = await bookingService.createWeek({ locationId: submittedLocationId, weekStartDate: date, timeSlot });
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
        ?? 'Failed to create booking.';
      setError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) {
    return <div className="flex h-64 items-center justify-center text-gray-400">Loading...</div>;
  }

  // Single-booking fallback summary (only shown when server reports a fallback)
  if (singleResult) {
    return (
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Booking Created</h1>
        <div className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
          <div className="mb-4 rounded-md bg-amber-50 px-4 py-3 text-sm text-amber-800">
            {singleResult.fallbackReason}
          </div>
          <dl className="space-y-2">
            <div className="flex justify-between text-sm">
              <dt className="text-gray-500">Location</dt>
              <dd className="font-medium text-gray-900">{singleResult.locationName}</dd>
            </div>
            <div className="flex justify-between text-sm">
              <dt className="text-gray-500">Date</dt>
              <dd className="font-medium text-gray-900">{formatDateDE(singleResult.date)}</dd>
            </div>
            <div className="flex justify-between text-sm">
              <dt className="text-gray-500">Time Slot</dt>
              <dd className="font-medium text-gray-900">{singleResult.timeSlot}</dd>
            </div>
          </dl>
          <button
            type="button"
            onClick={() => navigate('/my-bookings')}
            className="mt-6 rounded-md bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            Go to My Bookings
          </button>
        </div>
      </div>
    );
  }

  // Week booking result summary
  if (weekResult) {
    const fallbackBookings = weekResult.created.filter((b) => b.fallbackReason);
    return (
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Week Booking Summary</h1>
        <div className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
          <div className="mb-4 rounded-md bg-green-50 px-4 py-3 text-sm text-green-700">
            {weekResult.created.length} booking{weekResult.created.length !== 1 ? 's' : ''} created successfully.
          </div>
          {fallbackBookings.length > 0 && (
            <div className="mb-4">
              <h3 className="mb-2 text-sm font-medium text-gray-700">Days booked at a fallback location:</h3>
              <ul className="space-y-1">
                {fallbackBookings.map((b) => (
                  <li key={b.id} className="flex items-start gap-2 text-sm text-amber-800">
                    <span className="mt-1.5 inline-block h-2 w-2 flex-shrink-0 rounded-full bg-amber-400" />
                    <span>{formatDateDE(b.date)} → <strong>{b.locationName}</strong>. {b.fallbackReason}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {weekResult.skipped.length > 0 && (
            <div>
              <h3 className="mb-2 text-sm font-medium text-gray-700">Skipped days:</h3>
              <ul className="space-y-1">
                {weekResult.skipped.map((s) => (
                  <li key={s.date} className="flex items-center gap-2 text-sm text-amber-700">
                    <span className="inline-block h-2 w-2 rounded-full bg-amber-400" />
                    {formatDateDE(s.date)} — {s.reason}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <button
            type="button"
            onClick={() => navigate('/my-bookings')}
            className="mt-6 rounded-md bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            Go to My Bookings
          </button>
        </div>
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Book a Parking Spot</h1>

      {/* Step indicator */}
      <div className="mt-6 flex gap-2">
        {STEPS.map((label, i) => {
          const stepNum = i + 1;
          const isCurrent = step === stepNum;
          const isDone = step > stepNum;
          return (
            <button
              key={label}
              type="button"
              onClick={() => { if (isDone) setStep(stepNum); }}
              disabled={!isDone}
              className={`flex items-center gap-2 rounded-full px-3 py-1 text-sm font-medium transition-colors ${
                isCurrent
                  ? 'bg-blue-100 text-blue-800'
                  : isDone
                    ? 'bg-green-100 text-green-800 hover:bg-green-200'
                    : 'bg-gray-100 text-gray-400'
              }`}
            >
              <span className={`flex h-5 w-5 items-center justify-center rounded-full text-xs ${
                isCurrent ? 'bg-blue-500 text-white' : isDone ? 'bg-green-500 text-white' : 'bg-gray-300 text-white'
              }`}>
                {isDone ? '\u2713' : stepNum}
              </span>
              {label}
            </button>
          );
        })}
      </div>

      {error && (
        <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>
      )}

      <div className="mt-6">
        {/* Step 1: Location */}
        {step === 1 && (
          <div>
            <h2 className="mb-4 text-lg font-medium text-gray-700">Select a location</h2>
            <LocationSelector
              locations={locations}
              selectedId={locationId}
              onChange={handleLocationSelect}
            />
          </div>
        )}

        {/* Step 2: Date */}
        {step === 2 && (
          <div>
            <h2 className="mb-4 text-lg font-medium text-gray-700">
              Select a {weekMode ? 'week' : 'date'} at {selectedLocation?.name}
            </h2>

            {/* Week mode toggle */}
            <label className="mb-4 flex items-center gap-2 text-sm text-gray-600">
              <input
                type="checkbox"
                checked={weekMode}
                onChange={(e) => { setWeekMode(e.target.checked); setDate(null); }}
                className="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              />
              Book entire week (Mon - Fri)
            </label>

            <CalendarPicker
              selectedDate={date}
              onSelect={handleDateSelect}
              blockedDates={blockedDates}
              availability={availabilityMap}
              minDate={minDate}
              maxDate={maxDate}
              weekMode={weekMode}
            />
          </div>
        )}

        {/* Step 3: Time Slot */}
        {step === 3 && (
          <div>
            <h2 className="mb-4 text-lg font-medium text-gray-700">
              Select a time slot for {weekMode && date
                ? `${formatDateDE(date)} - ${formatDateDE(getWeekFriday(date))}`
                : date}
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
            <h2 className="mb-4 text-lg font-medium text-gray-700">Review your booking</h2>
            <div className="rounded-lg border border-gray-200 bg-white p-6">
              <dl className="space-y-3">
                <div className="flex justify-between">
                  <dt className="text-sm text-gray-500">Location</dt>
                  <dd className="text-sm font-medium text-gray-900">{selectedLocation?.name}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-sm text-gray-500">{weekMode ? 'Week' : 'Date'}</dt>
                  <dd className="text-sm font-medium text-gray-900">
                    {weekMode && date
                      ? `${formatDateDE(date)} - ${formatDateDE(getWeekFriday(date))}`
                      : date && new Date(date + 'T00:00:00').toLocaleDateString('de-DE', {
                          weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
                        })
                    }
                  </dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-sm text-gray-500">Time Slot</dt>
                  <dd className="text-sm font-medium text-gray-900">{timeSlot}</dd>
                </div>
                {weekMode && (
                  <div className="rounded-md bg-blue-50 px-3 py-2 text-xs text-blue-700">
                    Individual bookings will be created for each weekday. Days that are blocked or already booked will be skipped.
                  </div>
                )}
              </dl>

              {gridAvailability && !weekMode && (
                <div className="mt-6">
                  <h3 className="mb-2 text-sm font-medium text-gray-700">Parking Layout</h3>
                  <ParkingGridView availability={gridAvailability} />
                </div>
              )}

              <div className="mt-6 flex gap-3">
                <button
                  type="button"
                  onClick={() => setStep(1)}
                  className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
                >
                  Start over
                </button>
                <button
                  type="button"
                  onClick={handleSubmit}
                  disabled={isSubmitting}
                  className="rounded-md bg-blue-600 px-6 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
                >
                  {isSubmitting
                    ? 'Booking...'
                    : weekMode
                      ? 'Book Entire Week'
                      : 'Confirm Booking'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
