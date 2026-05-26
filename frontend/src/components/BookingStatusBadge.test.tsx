import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { BookingStatusBadge } from './BookingStatusBadge';

describe('BookingStatusBadge', () => {
  it.each([
    ['Pending', 'Pending', 'amber'],
    ['Won', 'Won — Confirm', 'emerald'],
    ['Lost', 'Lost', 'rose'],
    ['Confirmed', 'Confirmed', 'brand'],
    ['Cancelled', 'Cancelled', 'ink'],
    ['Expired', 'Expired', 'ink'],
  ] as const)('renders %s status with correct label and color', (status, label, color) => {
    render(<BookingStatusBadge status={status} />);
    const badge = screen.getByText(label);
    expect(badge).toBeInTheDocument();
    expect(badge.className).toContain(color);
  });
});
