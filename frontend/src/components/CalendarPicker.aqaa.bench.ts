import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { bench, describe } from 'vitest';
import '../i18n';
import { CalendarPicker } from './CalendarPicker';
import type { DayAvailability, SlotDemand } from '../types/booking';
import { todayInBerlin } from '../utils/berlinTime';

const noop = () => {};
const today = todayInBerlin();
const [ty, tm] = today.split('-').map(Number);

function dateAt(offset: number): string {
  const d = new Date(ty, tm - 1, 1 + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function demand(i: number): SlotDemand {
  return { available: i % 15, total: 15, pending: i % 20, waitlist: 0, lotteryRan: i % 2 === 0 };
}

function input(n: number) {
  const blocked = new Set<string>();
  const availability = new Map<string, DayAvailability>();
  for (let i = 0; i < n; i++) {
    if (i % 3 === 0) blocked.add(dateAt(i - Math.floor(n / 2)));
    availability.set(dateAt(i - Math.floor(n / 2)), { morning: demand(i), afternoon: demand(i + 7) });
  }
  return { blocked, availability };
}

const sizes = [31, 365, 3650];
const inputs = sizes.map((n) => ({ n, ...input(n) }));
let sink = 0;

describe('CalendarPicker render (day mode, blocked/availability size)', () => {
  for (const { n, blocked, availability } of inputs) {
    bench(`days=${n}`, () => {
      sink += renderToStaticMarkup(
        createElement(CalendarPicker, {
          selectedDate: today,
          onSelect: noop,
          blockedDates: blocked,
          availability,
          minDate: dateAt(-5),
          maxDate: dateAt(20),
        }),
      ).length;
    });
  }
});

describe('CalendarPicker render (week mode, blocked/availability size)', () => {
  for (const { n, blocked, availability } of inputs) {
    bench(`days=${n}`, () => {
      sink += renderToStaticMarkup(
        createElement(CalendarPicker, {
          selectedDate: today,
          onSelect: noop,
          blockedDates: blocked,
          availability,
          weekMode: true,
        }),
      ).length;
    });
  }
});

export { sink };
