import { describe, it, expect, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LicensePlateInput } from './LicensePlateInput';
import { EMPTY_PLATE, composePlate, type PlateValue } from './plate';

/** Stateful host so typing flows back into the controlled component; reports every change. */
function Harness({ initial = EMPTY_PLATE, spy }: { initial?: PlateValue; spy: (v: PlateValue) => void }) {
  const [value, setValue] = useState<PlateValue>(initial);
  return (
    <LicensePlateInput
      value={value}
      onChange={(v) => {
        spy(v);
        setValue(v);
      }}
    />
  );
}

function lastValue(spy: ReturnType<typeof vi.fn>): PlateValue {
  return spy.mock.calls[spy.mock.calls.length - 1][0] as PlateValue;
}

describe('LicensePlateInput', () => {
  it('shows the German three-part plate by default with Germany checked', () => {
    render(<Harness spy={vi.fn()} />);

    expect(screen.getByRole('radio', { name: 'Germany' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: 'Austria' })).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByLabelText('District code')).toBeInTheDocument();
    expect(screen.getByLabelText('Letters')).toBeInTheDocument();
    expect(screen.getByLabelText('Digits')).toBeInTheDocument();
    expect(screen.queryByLabelText('Licence plate')).not.toBeInTheDocument();
  });

  it('uppercases the district and drops non-letters, keeping umlauts, at most 3 letters', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);

    await user.type(screen.getByLabelText('District code'), 'gö1-pxz');

    expect(screen.getByLabelText('District code')).toHaveValue('GÖP');
    expect(lastValue(spy).district).toBe('GÖP');
  });

  it('limits the letters field to 2 letters', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);

    await user.type(screen.getByLabelText('Letters'), 'a b9cd');

    expect(screen.getByLabelText('Letters')).toHaveValue('AB');
  });

  it('keeps only digits, at most 4', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);

    await user.type(screen.getByLabelText('Digits'), '12a34 5');

    expect(screen.getByLabelText('Digits')).toHaveValue('1234');
    expect(lastValue(spy).digits).toBe('1234');
  });

  it('truncates pasted input that exceeds the limits', () => {
    const spy = vi.fn();
    render(<Harness spy={spy} />);

    fireEvent.change(screen.getByLabelText('Digits'), { target: { value: '9876543' } });
    fireEvent.change(screen.getByLabelText('District code'), { target: { value: 'abcdef' } });

    expect(screen.getByLabelText('Digits')).toHaveValue('9876');
    expect(screen.getByLabelText('District code')).toHaveValue('ABC');
  });

  it('composes the full German plate from the three fields', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);

    await user.type(screen.getByLabelText('District code'), 'gp');
    await user.type(screen.getByLabelText('Letters'), 'ab');
    await user.type(screen.getByLabelText('Digits'), '1234');
    await user.click(screen.getByRole('radio', { name: /electric/i }));

    const value = lastValue(spy);
    expect(value).toMatchObject({ country: 'DE', type: 'Electric', district: 'GP', letters: 'AB', digits: '1234' });
    expect(composePlate(value)).toBe('GP-AB 1234 E');
  });

  it('shows the suffix next to the digits for historic and electric plates only', async () => {
    const user = userEvent.setup();
    render(<Harness spy={vi.fn()} />);

    await user.click(screen.getByRole('radio', { name: /historic/i }));
    expect(screen.getByRole('radio', { name: /historic/i })).toHaveAttribute('aria-checked', 'true');
    // "H" appears in the type button badge and next to the plate digits.
    expect(screen.getAllByText('H')).toHaveLength(2);

    await user.click(screen.getByRole('radio', { name: /standard/i }));
    expect(screen.getAllByText('H')).toHaveLength(1);
  });

  it('switches to a single free-text field for other countries and uppercases it', async () => {
    const user = userEvent.setup();
    const spy = vi.fn();
    render(<Harness spy={spy} />);

    await user.click(screen.getByRole('radio', { name: 'Austria' }));
    expect(lastValue(spy).country).toBe('AT');
    expect(screen.queryByLabelText('District code')).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Licence plate'), 'w-12345x');
    expect(screen.getByLabelText('Licence plate')).toHaveValue('W-12345X');
    expect(composePlate(lastValue(spy))).toBe('W-12345X');
  });

  it('caps the free-text field at 20 characters', () => {
    const spy = vi.fn();
    render(<Harness initial={{ ...EMPTY_PLATE, country: 'OTHER' }} spy={spy} />);

    fireEvent.change(screen.getByLabelText('Licence plate'), { target: { value: 'abcdefghij0123456789xyz' } });

    expect(lastValue(spy).free).toBe('ABCDEFGHIJ0123456789');
  });

  it('keeps the other fields when one field changes', () => {
    const spy = vi.fn();
    const initial: PlateValue = { ...EMPTY_PLATE, district: 'GP', letters: 'AB', digits: '12', free: 'X' };
    render(<LicensePlateInput value={initial} onChange={spy} />);

    fireEvent.change(screen.getByLabelText('Digits'), { target: { value: '123' } });

    expect(spy).toHaveBeenCalledWith({ ...initial, digits: '123' });
  });
});
