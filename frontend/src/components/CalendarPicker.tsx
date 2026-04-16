import { useState, useMemo } from 'react';

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

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

function toDateStr(year: number, month: number, day: number): string {
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function parseDate(str: string): Date {
  const [y, m, d] = str.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function CalendarPicker({ selectedDate, onSelect, blockedDates, availability, minDate, maxDate, weekMode }: Props) {
  const today = new Date();
  const [viewYear, setViewYear] = useState(today.getFullYear());
  const [viewMonth, setViewMonth] = useState(today.getMonth());

  const days = useMemo(() => {
    const firstDay = new Date(viewYear, viewMonth, 1);
    // Monday = 0, Sunday = 6
    let startDow = firstDay.getDay() - 1;
    if (startDow < 0) startDow = 6;

    const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
    const cells: (number | null)[] = [];

    for (let i = 0; i < startDow; i++) cells.push(null);
    for (let d = 1; d <= daysInMonth; d++) cells.push(d);

    return cells;
  }, [viewYear, viewMonth]);

  const prevMonth = () => {
    if (viewMonth === 0) { setViewYear(viewYear - 1); setViewMonth(11); }
    else setViewMonth(viewMonth - 1);
  };

  const nextMonth = () => {
    if (viewMonth === 11) { setViewYear(viewYear + 1); setViewMonth(0); }
    else setViewMonth(viewMonth + 1);
  };

  const monthLabel = new Date(viewYear, viewMonth).toLocaleString('en-US', { month: 'long', year: 'numeric' });

  return (
    <div className="w-full max-w-sm">
      <div className="mb-3 flex items-center justify-between">
        <button type="button" onClick={prevMonth} className="rounded p-1 hover:bg-gray-100">
          <svg className="h-5 w-5 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
          </svg>
        </button>
        <span className="font-semibold text-gray-900">{monthLabel}</span>
        <button type="button" onClick={nextMonth} className="rounded p-1 hover:bg-gray-100">
          <svg className="h-5 w-5 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
          </svg>
        </button>
      </div>

      <div className="grid grid-cols-7 gap-1 text-center text-xs font-medium text-gray-500">
        {WEEKDAYS.map((d) => <div key={d} className="py-1">{d}</div>)}
      </div>

      <div className="grid grid-cols-7 gap-1">
        {days.map((day, i) => {
          if (day === null) return <div key={`empty-${i}`} />;

          const dateStr = toDateStr(viewYear, viewMonth, day);
          const date = parseDate(dateStr);
          const isBlocked = blockedDates.has(dateStr);
          const isWeekend = date.getDay() === 0 || date.getDay() === 6;
          const isSelected = weekMode && selectedDate
            ? getWeekDays(getMonday(selectedDate)).includes(dateStr)
            : selectedDate === dateStr;
          const isPast = minDate ? dateStr < minDate : date <= today;
          const isFuture = maxDate ? dateStr > maxDate : false;
          const isDisabled = isPast || isFuture || isBlocked || (weekMode && isWeekend);

          const avail = availability?.get(dateStr);
          const totalAvail = avail ? avail.morning + avail.afternoon : undefined;

          return (
            <button
              key={dateStr}
              type="button"
              disabled={isDisabled}
              onClick={() => onSelect(weekMode ? getMonday(dateStr) : dateStr)}
              className={`relative flex h-10 items-center justify-center rounded text-sm transition-colors ${
                isDisabled
                  ? isBlocked
                    ? 'cursor-not-allowed bg-red-50 text-red-300 line-through'
                    : 'cursor-not-allowed text-gray-300'
                  : isSelected
                    ? 'bg-blue-500 font-semibold text-white'
                    : 'text-gray-700 hover:bg-gray-100'
              }`}
            >
              {day}
              {!isDisabled && totalAvail !== undefined && (
                <span className={`absolute bottom-0.5 left-1/2 h-1.5 w-1.5 -translate-x-1/2 rounded-full ${
                  totalAvail > 10 ? 'bg-green-400' : totalAvail > 0 ? 'bg-amber-400' : 'bg-red-400'
                }`} />
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}
