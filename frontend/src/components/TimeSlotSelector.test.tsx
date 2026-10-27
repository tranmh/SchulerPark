import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import i18n from '../i18n';
import { TimeSlotSelector } from './TimeSlotSelector';
import type { SlotDemand, TimeSlot } from '../types/booking';

function demand(d: Pick<SlotDemand, 'total' | 'pending' | 'available' | 'lotteryRan'>): SlotDemand {
  return d as SlotDemand;
}

function slotButton(slot: TimeSlot): HTMLButtonElement {
  const label = i18n.t(`components.timeSlot.${slot}`);
  const btn = screen.getByText(label).closest('button');
  if (!btn) throw new Error(`no button for ${slot}`);
  return btn;
}

describe('TimeSlotSelector', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('calls onChange with the clicked slot', () => {
    const onChange = vi.fn<(s: TimeSlot) => void>();
    render(<TimeSlotSelector value={null} onChange={onChange} />);
    fireEvent.click(slotButton('Afternoon'));
    fireEvent.click(slotButton('Morning'));
    expect(onChange.mock.calls).toEqual([['Afternoon'], ['Morning']]);
  });

  it('disables a slot that is closed today and labels it', () => {
    const onChange = vi.fn<(s: TimeSlot) => void>();
    render(<TimeSlotSelector value={null} onChange={onChange} closedSlots={['Morning']} />);
    const morning = slotButton('Morning');
    expect(morning).toBeDisabled();
    expect(morning).toHaveAttribute('title', i18n.t('components.timeSlot.closedToday'));
    fireEvent.click(morning);
    expect(onChange).not.toHaveBeenCalled();
    expect(slotButton('Afternoon')).toBeEnabled();
  });

  it('shows pre-lottery demand with the lottery time and keeps the slot bookable even when oversubscribed', () => {
    render(
      <TimeSlotSelector
        value={null}
        onChange={vi.fn()}
        lotteryTime="20:30"
        demand={{ morning: demand({ total: 10, pending: 12, available: 0, lotteryRan: false }) }}
      />,
    );
    expect(screen.getByText(i18n.t('components.timeSlot.requests', { count: 12, total: 10 }))).toBeInTheDocument();
    expect(screen.getByText(i18n.t('components.timeSlot.lotteryPending', { time: '20:30' }))).toBeInTheDocument();
    expect(slotButton('Morning')).toBeEnabled();
  });

  it('disables a slot that is full after the lottery', () => {
    render(
      <TimeSlotSelector
        value={null}
        onChange={vi.fn()}
        demand={{ afternoon: demand({ total: 10, pending: 0, available: 0, lotteryRan: true }) }}
      />,
    );
    expect(slotButton('Afternoon')).toBeDisabled();
    expect(screen.getByText(i18n.t('components.timeSlot.full'))).toBeInTheDocument();
  });

  it('disables a slot without any capacity', () => {
    render(
      <TimeSlotSelector
        value={null}
        onChange={vi.fn()}
        demand={{ morning: demand({ total: 0, pending: 0, available: 0, lotteryRan: false }) }}
      />,
    );
    expect(slotButton('Morning')).toBeDisabled();
  });

  it('shows free slots after the lottery', () => {
    render(
      <TimeSlotSelector
        value="Morning"
        onChange={vi.fn()}
        demand={{ morning: demand({ total: 10, pending: 0, available: 1, lotteryRan: true }) }}
      />,
    );
    expect(screen.getByText(i18n.t('components.timeSlot.available', { count: 1 }))).toBeInTheDocument();
    expect(slotButton('Morning')).toBeEnabled();
  });
});
