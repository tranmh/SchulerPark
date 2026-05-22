import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';

interface Props {
  selectedDate: string | null;
  onSelect: (date: string) => void;
  blockedDates: Set<string>;
  availability?: Map<string, { morning: number; afternoon: number }>;
  minDate?: string;
  maxDate?: string;
  weekMode?: boolean;
}

function getMonday(dateStr: string): string {
  const d = parseDate(dateStr);
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day; // Monday=1
  d.setDate(d.getDate() + diff);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function getWeekDays(mondayStr: string): string[] {
  const d = parseDate(mondayStr);
  return Array.from({ length: 5 }, (_, i) => {
    const day = new Date(d);
    day.setDate(day.getDate() + i);
    return `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`;
  });
}

function toDateStr(year: number, month: number, day: number): string {
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function parseDate(str: string): Date {
  const [y, m, d] = str.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function CalendarPicker({
  selectedDate,
  onSelect,
  blockedDates,
  availability,
  minDate,
  maxDate,
  weekMode,
}: Props) {
  const { i18n, t } = useTranslation();
  const today = new Date();
  const [viewYear, setViewYear] = useState(today.getFullYear());
  const [viewMonth, setViewMonth] = useState(today.getMonth());

  const locale = i18n.language.startsWith('de') ? 'de-DE' : 'en-US';
  const weekdayLocale = i18n.language.startsWith('de')
    ? ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So']
    : ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];

  const days = useMemo(() => {
    const firstDay = new Date(viewYear, viewMonth, 1);
    let startDow = firstDay.getDay() - 1;
    if (startDow < 0) startDow = 6;

    const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
    const cells: (number | null)[] = [];

    for (let i = 0; i < startDow; i++) cells.push(null);
    for (let d = 1; d <= daysInMonth; d++) cells.push(d);

    return cells;
  }, [viewYear, viewMonth]);

  const prevMonth = () => {
    if (viewMonth === 0) {
      setViewYear(viewYear - 1);
      setViewMonth(11);
    } else setViewMonth(viewMonth - 1);
  };

  const nextMonth = () => {
    if (viewMonth === 11) {
      setViewYear(viewYear + 1);
      setViewMonth(0);
    } else setViewMonth(viewMonth + 1);
  };

  const monthLabel = new Date(viewYear, viewMonth).toLocaleString(locale, { month: 'long', year: 'numeric' });

  return (
    <div className="w-full max-w-sm rounded-card border border-line bg-white p-5 shadow-card">
      <div className="mb-4 flex items-center justify-between">
        <div className="text-[14px] font-semibold tracking-tight text-ink-900">{monthLabel}</div>
        <div className="flex gap-1">
          <button
            type="button"
            onClick={prevMonth}
            aria-label={t('components.calendar.prevMonth')}
            className="grid h-8 w-8 place-items-center rounded-md text-ink-500 transition-colors hover:bg-line/60 hover:text-ink-900"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
            </svg>
          </button>
          <button
            type="button"
            onClick={nextMonth}
            aria-label={t('components.calendar.nextMonth')}
            className="grid h-8 w-8 place-items-center rounded-md text-ink-500 transition-colors hover:bg-line/60 hover:text-ink-900"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
            </svg>
          </button>
        </div>
      </div>

      <div className="grid grid-cols-7 gap-1 text-center text-[11px] font-semibold uppercase tracking-wider text-ink-400">
        {weekdayLocale.map((d) => (
          <div key={d} className="py-1">
            {d}
          </div>
        ))}
      </div>

      <div className="mt-1 grid grid-cols-7 gap-1 num">
        {days.map((day, i) => {
          if (day === null) return <div key={`empty-${i}`} className="h-10" />;

          const dateStr = toDateStr(viewYear, viewMonth, day);
          const date = parseDate(dateStr);
          const isBlocked = blockedDates.has(dateStr);
          const isWeekend = date.getDay() === 0 || date.getDay() === 6;
          const isSelected =
            weekMode && selectedDate
              ? getWeekDays(getMonday(selectedDate)).includes(dateStr)
              : selectedDate === dateStr;
          const isPast = minDate ? dateStr < minDate : date <= today;
          const isFuture = maxDate ? dateStr > maxDate : false;
          const isDisabled = isPast || isFuture || isBlocked || (weekMode && isWeekend);

          const avail = availability?.get(dateStr);
          const totalAvail = avail ? avail.morning + avail.afternoon : undefined;

          let cls = 'relative flex h-10 items-center justify-center rounded-md text-[13px] transition-colors';
          if (isDisabled) {
            cls += isBlocked
              ? ' cursor-not-allowed bg-rose-50 text-rose-300 line-through'
              : ' cursor-not-allowed text-ink-200';
          } else if (isSelected) {
            cls += ' bg-brand-500 font-semibold text-white shadow-sm';
          } else {
            cls += ' text-ink-700 hover:bg-line/60';
          }

          return (
            <button
              key={dateStr}
              type="button"
              disabled={isDisabled}
              onClick={() => onSelect(weekMode ? getMonday(dateStr) : dateStr)}
              className={cls}
            >
              {day}
              {!isDisabled && totalAvail !== undefined && (
                <span
                  className={`absolute bottom-1 left-1/2 h-1 w-1 -translate-x-1/2 rounded-full ${
                    isSelected
                      ? 'bg-white/80'
                      : totalAvail > 10
                        ? 'bg-emerald-500'
                        : totalAvail > 0
                          ? 'bg-amber-500'
                          : 'bg-rose-500'
                  }`}
                />
              )}
            </button>
          );
        })}
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-x-4 gap-y-1.5 border-t border-line pt-3 text-[11px] text-ink-500">
        <div className="flex items-center gap-1.5">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
          {t('components.calendar.plenty')}
        </div>
        <div className="flex items-center gap-1.5">
          <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
          {t('components.calendar.limited')}
        </div>
        <div className="flex items-center gap-1.5">
          <span className="h-1.5 w-1.5 rounded-full bg-rose-500" />
          {t('components.calendar.almostFull')}
        </div>
        <div className="ml-auto flex items-center gap-1.5">
          <span className="h-3 w-3 rounded-sm bg-rose-50 ring-1 ring-rose-200" />
          {t('components.calendar.blocked')}
        </div>
      </div>
    </div>
  );
}
