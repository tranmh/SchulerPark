import type { TimeSlot } from '../types/booking';

interface Props {
  value: TimeSlot | null;
  onChange: (slot: TimeSlot) => void;
  morningAvailable?: number;
  afternoonAvailable?: number;
}

export function TimeSlotSelector({ value, onChange, morningAvailable, afternoonAvailable }: Props) {
  const slots: { slot: TimeSlot; label: string; time: string; available?: number }[] = [
    { slot: 'Morning', label: 'Morning', time: '06:00 – 12:00', available: morningAvailable },
    { slot: 'Afternoon', label: 'Afternoon', time: '12:00 – 18:00', available: afternoonAvailable },
  ];

  return (
    <div className="flex gap-4">
      {slots.map(({ slot, label, time, available }) => {
        const isSelected = value === slot;
        const isDisabled = available !== undefined && available <= 0;

        return (
          <button
            key={slot}
            type="button"
            disabled={isDisabled}
            onClick={() => onChange(slot)}
            className={`flex-1 rounded-lg border-2 p-4 text-left transition-colors ${
              isDisabled
                ? 'cursor-not-allowed border-gray-200 bg-gray-50 text-gray-400'
                : isSelected
                  ? 'border-blue-500 bg-blue-50 text-blue-900'
                  : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300'
            }`}
          >
            <div className="font-semibold">{label}</div>
            <div className="text-sm opacity-70">{time}</div>
            {available !== undefined && (
              <div className={`mt-1 text-sm ${available > 0 ? 'text-green-600' : 'text-red-500'}`}>
                {available > 0 ? `${available} slot${available !== 1 ? 's' : ''} available` : 'Full'}
              </div>
            )}
          </button>
        );
      })}
    </div>
  );
}
