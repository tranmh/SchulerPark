import { useEffect, useState, useMemo } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { bookingService } from '../../services/bookingService';
import { locationService } from '../../services/locationService';
import { LocationSelector } from '../../components/LocationSelector';
import { CalendarPicker } from '../../components/CalendarPicker';
import { TimeSlotSelector } from '../../components/TimeSlotSelector';
import type { Location, Availability, TimeSlot } from '../../types/booking';

const STEPS = ['Location', 'Date', 'Time Slot', 'Confirm'];

export function BookingPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const preselectedLocation = searchParams.get('location');

  const [step, setStep] = useState(preselectedLocation ? 2 : 1);
  const [locationId, setLocationId] = useState<string | null>(preselectedLocation);
  const [date, setDate] = useState<string | null>(null);
  const [timeSlot, setTimeSlot] = useState<TimeSlot | null>(null);

  const [locations, setLocations] = useState<Location[]>([]);
  const [availability, setAvailability] = useState<Availability[]>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // Load locations
  useEffect(() => {
    locationService.getLocations()
      .then(setLocations)
      .catch(() => setError('Failed to load locations.'))
      .finally(() => setLoading(false));
  }, []);

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
    setDate(null);
    setTimeSlot(null);
    setStep(2);
    setError(null);
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
  };

  const handleSubmit = async () => {
    if (!locationId || !date || !timeSlot) return;
    setIsSubmitting(true);
    setError(null);
    try {
      await bookingService.create({ locationId, date, timeSlot });
      navigate('/my-bookings');
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
              Select a date at {selectedLocation?.name}
            </h2>
            <CalendarPicker
              selectedDate={date}
              onSelect={handleDateSelect}
              blockedDates={blockedDates}
              availability={availabilityMap}
              minDate={minDate}
              maxDate={maxDate}
            />
          </div>
        )}

        {/* Step 3: Time Slot */}
        {step === 3 && (
          <div>
            <h2 className="mb-4 text-lg font-medium text-gray-700">
              Select a time slot for {date}
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
                  <dt className="text-sm text-gray-500">Date</dt>
                  <dd className="text-sm font-medium text-gray-900">
                    {date && new Date(date + 'T00:00:00').toLocaleDateString('de-DE', {
                      weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
                    })}
                  </dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-sm text-gray-500">Time Slot</dt>
                  <dd className="text-sm font-medium text-gray-900">{timeSlot}</dd>
                </div>
              </dl>

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
                  {isSubmitting ? 'Booking...' : 'Confirm Booking'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
