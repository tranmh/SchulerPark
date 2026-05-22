import { useTranslation } from 'react-i18next';
import type { Location } from '../types/booking';

interface Props {
  locations: Location[];
  selectedId: string | null;
  onChange: (locationId: string) => void;
}

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .join('');
}

export function LocationSelector({ locations, selectedId, onChange }: Props) {
  const { t } = useTranslation();
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {locations.map((loc) => {
        const isSelected = selectedId === loc.id;
        return (
          <button
            key={loc.id}
            type="button"
            onClick={() => onChange(loc.id)}
            className={`group flex w-full items-start gap-4 rounded-card border-2 p-5 text-left transition-all ${
              isSelected
                ? 'border-brand-500 bg-brand-50/50 shadow-card'
                : 'border-line bg-white hover:border-line-strong hover:bg-surface-warm'
            }`}
          >
            <div
              className={`grid h-11 w-11 shrink-0 place-items-center rounded-lg text-[13px] font-bold ${
                isSelected ? 'bg-brand-500 text-white' : 'bg-brand-50 text-brand-700'
              }`}
            >
              {initials(loc.name)}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <div className="text-[15px] font-semibold text-ink-900">{loc.name}</div>
                {isSelected && (
                  <svg className="h-4 w-4 text-brand-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 13l4 4L19 7" />
                  </svg>
                )}
              </div>
              <div className="mt-0.5 truncate text-[12.5px] text-ink-400">{loc.address}</div>
              <div className="mt-3 inline-flex items-center gap-1.5 text-[12px] font-medium text-ink-500">
                <svg className="h-3.5 w-3.5 text-ink-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5" />
                </svg>
                <span className="num font-semibold text-ink-700">{loc.totalSlots}</span>
                <span>{t('components.location.slots', { count: loc.totalSlots })}</span>
              </div>
            </div>
          </button>
        );
      })}
    </div>
  );
}
