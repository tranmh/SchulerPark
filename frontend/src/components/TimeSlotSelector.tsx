import type { ReactElement } from 'react';
import { useTranslation } from 'react-i18next';
import type { TimeSlot } from '../types/booking';

interface Props {
  value: TimeSlot | null;
  onChange: (slot: TimeSlot) => void;
  morningAvailable?: number;
  afternoonAvailable?: number;
}

interface SlotDef {
  slot: TimeSlot;
  label: string;
  time: string;
  available?: number;
  icon: ReactElement;
}

const SunIcon = (
  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <circle cx="12" cy="12" r="4" strokeWidth={1.5} />
    <path strokeLinecap="round" strokeWidth={1.5} d="M12 3v1.5M12 19.5V21M3 12h1.5M19.5 12H21M5.6 5.6l1.1 1.1M17.3 17.3l1.1 1.1M5.6 18.4l1.1-1.1M17.3 6.7l1.1-1.1" />
  </svg>
);

const MoonIcon = (
  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z" />
  </svg>
);

export function TimeSlotSelector({ value, onChange, morningAvailable, afternoonAvailable }: Props) {
  const { t } = useTranslation();
  const slots: SlotDef[] = [
    { slot: 'Morning',   label: t('components.timeSlot.Morning'),   time: t('components.timeSlot.morningRange'),   available: morningAvailable,   icon: SunIcon },
    { slot: 'Afternoon', label: t('components.timeSlot.Afternoon'), time: t('components.timeSlot.afternoonRange'), available: afternoonAvailable, icon: MoonIcon },
  ];

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {slots.map(({ slot, label, time, available, icon }) => {
        const isSelected = value === slot;
        const isDisabled = available !== undefined && available <= 0;

        let cls =
          'flex w-full items-start gap-4 rounded-card border-2 p-5 text-left transition-all';
        if (isDisabled) {
          cls += ' cursor-not-allowed border-line bg-surface-sunken text-ink-400';
        } else if (isSelected) {
          cls += ' border-brand-500 bg-brand-50/50 text-brand-900 shadow-card';
        } else {
          cls += ' border-line bg-white text-ink-700 hover:border-line-strong hover:bg-surface-warm';
        }

        return (
          <button key={slot} type="button" disabled={isDisabled} onClick={() => onChange(slot)} className={cls}>
            <div
              className={`grid h-10 w-10 shrink-0 place-items-center rounded-lg ${
                isDisabled
                  ? 'bg-line text-ink-400'
                  : isSelected
                    ? 'bg-brand-500 text-white'
                    : 'bg-brand-50 text-brand-600'
              }`}
            >
              {icon}
            </div>
            <div className="flex-1">
              <div className="flex items-center gap-2">
                <div className="text-[15px] font-semibold">{label}</div>
                {isSelected && (
                  <svg className="h-4 w-4 text-brand-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 13l4 4L19 7" />
                  </svg>
                )}
              </div>
              <div className="mt-0.5 text-[12.5px] text-ink-400 num">{time}</div>
              {available !== undefined && (
                <div
                  className={`mt-2 inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11.5px] font-semibold ${
                    available > 10
                      ? 'bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200'
                      : available > 0
                        ? 'bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200'
                        : 'bg-rose-50 text-rose-700 ring-1 ring-inset ring-rose-200'
                  }`}
                >
                  <span
                    className={`h-1.5 w-1.5 rounded-full ${
                      available > 10 ? 'bg-emerald-500' : available > 0 ? 'bg-amber-500' : 'bg-rose-500'
                    }`}
                  />
                  <span className="num">
                    {available > 0 ? t('components.timeSlot.available', { count: available }) : t('components.timeSlot.full')}
                  </span>
                </div>
              )}
            </div>
          </button>
        );
      })}
    </div>
  );
}
