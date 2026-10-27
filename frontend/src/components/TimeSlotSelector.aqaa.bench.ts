import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { bench, describe } from 'vitest';
import '../i18n';
import { TimeSlotSelector } from './TimeSlotSelector';
import type { SlotDemand, TimeSlot } from '../types/booking';

const noop = () => {};
let sink = 0;

const variants: { name: string; demand?: { morning?: SlotDemand; afternoon?: SlotDemand }; closed: TimeSlot[] }[] = [
  { name: 'no demand', demand: undefined, closed: [] },
  {
    name: 'pre-lottery demand',
    demand: {
      morning: { available: 10, total: 10, pending: 12, waitlist: 0, lotteryRan: false },
      afternoon: { available: 10, total: 10, pending: 3, waitlist: 0, lotteryRan: false },
    },
    closed: [],
  },
  {
    name: 'post-lottery, morning closed',
    demand: {
      morning: { available: 0, total: 10, pending: 0, waitlist: 2, lotteryRan: true },
      afternoon: { available: 4, total: 10, pending: 0, waitlist: 0, lotteryRan: true },
    },
    closed: ['Morning'],
  },
];

describe('TimeSlotSelector render', () => {
  for (const v of variants) {
    bench(v.name, () => {
      sink += renderToStaticMarkup(
        createElement(TimeSlotSelector, { value: 'Afternoon', onChange: noop, demand: v.demand, closedSlots: v.closed }),
      ).length;
    });
  }
});

export { sink };
