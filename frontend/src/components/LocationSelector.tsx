import type { Location } from '../types/booking';

interface Props {
  locations: Location[];
  selectedId: string | null;
  onChange: (locationId: string) => void;
}

export function LocationSelector({ locations, selectedId, onChange }: Props) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {locations.map((loc) => {
        const isSelected = selectedId === loc.id;
        return (
          <button
            key={loc.id}
            type="button"
            onClick={() => onChange(loc.id)}
            className={`rounded-lg border-2 p-4 text-left transition-colors ${
              isSelected
                ? 'border-blue-500 bg-blue-50'
                : 'border-gray-200 bg-white hover:border-gray-300'
            }`}
          >
            <div className="font-semibold text-gray-900">{loc.name}</div>
            <div className="mt-1 text-sm text-gray-500">{loc.address}</div>
            <div className="mt-2 text-sm text-gray-600">
              {loc.totalSlots} parking slot{loc.totalSlots !== 1 ? 's' : ''}
            </div>
          </button>
        );
      })}
    </div>
  );
}
