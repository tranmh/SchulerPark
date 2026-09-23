import type { ReactElement } from 'react';
import { useTranslation } from 'react-i18next';
import type { SlotDemand, TimeSlot } from '../types/booking';

interface Props {
  value: TimeSlot | null;
  onChange: (slot: TimeSlot) => void;
  /** Per-slot figures for the selected day; undefined while loading / unknown. */
  demand?: { morning?: SlotDemand; afternoon?: SlotDemand };
  /** Slots that can no longer be booked today (Berlin wall clock past the slot end). */
  closedSlots?: TimeSlot[];
}

interface SlotDef {
  slot: TimeSlot;
  label: string;
  time: string;
  demand?: SlotDemand;
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

type Tone = 'emerald' | 'amber' | 'rose';

const pillClass: Record<Tone, string> = {
  emerald: 'bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200',
  amber: 'bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200',
  rose: 'bg-rose-50 text-rose-700 ring-1 ring-inset ring-rose-200',
};
const dotClass: Record<Tone, string> = { emerald: 'bg-emerald-500', amber: 'bg-amber-500', rose: 'bg-rose-500' };

export function TimeSlotSelector({ value, onChange, demand, closedSlots = [] }: Props) {
  const { t } = useTranslation();
  const slots: SlotDef[] = [
    { slot: 'Morning',   label: t('components.timeSlot.Morning'),   time: t('components.timeSlot.morningRange'),   demand: demand?.morning,   icon: SunIcon },
    { slot: 'Afternoon', label: t('components.timeSlot.Afternoon'), time: t('components.timeSlot.afternoonRange'), demand: demand?.afternoon, icon: MoonIcon },
  ];

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {slots.map(({ slot, label, time, demand: d, icon }) => {
        const isSelected = value === slot;
        const isClosed = closedSlots.includes(slot);

        // WP3 3.2: before the lottery show demand, after it free slots.
        let pill: { tone: Tone; text: string; hint?: string } | null = null;
        let isFull = false;
        if (d) {
          if (d.total <= 0) {
            isFull = true;
            pill = { tone: 'rose', text: t('components.timeSlot.full') };
          } else if (!d.lotteryRan) {
            pill = {
              tone: d.pending >= d.total ? 'amber' : 'emerald',
              text: t('components.timeSlot.requests', { count: d.pending, total: d.total }),
              hint: t('components.timeSlot.lotteryPending'),
            };
          } else if (d.available <= 0) {
            isFull = true;
            pill = { tone: 'rose', text: t('components.timeSlot.full') };
          } else {
            pill = {
              tone: d.available > 10 ? 'emerald' : 'amber',
              text: t('components.timeSlot.available', { count: d.available }),
            };
          }
        }
        const isDisabled = isClosed || isFull;

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
          <button
            key={slot}
            type="button"
            disabled={isDisabled}
            onClick={() => onChange(slot)}
            title={isClosed ? t('components.timeSlot.closedToday') : undefined}
            className={cls}
          >
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
              {isClosed ? (
                <div className="mt-2 inline-flex items-center gap-1.5 rounded-full bg-ink-100 px-2 py-0.5 text-[11.5px] font-semibold text-ink-500 ring-1 ring-inset ring-line">
                  <span className="h-1.5 w-1.5 rounded-full bg-ink-300" />
                  {t('components.timeSlot.closedToday')}
                </div>
              ) : pill && (
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <div className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11.5px] font-semibold ${pillClass[pill.tone]}`}>
                    <span className={`h-1.5 w-1.5 rounded-full ${dotClass[pill.tone]}`} />
                    <span className="num">{pill.text}</span>
                  </div>
                  {pill.hint && <span className="text-[11.5px] text-ink-400">{pill.hint}</span>}
                </div>
              )}
            </div>
          </button>
        );
      })}
    </div>
  );
}
