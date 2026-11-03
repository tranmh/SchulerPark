import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LicensePlateInput } from './LicensePlateInput';
import { EMPTY_PLATE, type PlateValue } from './plate';

function setup(value: PlateValue = EMPTY_PLATE) {
  const onChange = vi.fn<(v: PlateValue) => void>();
  render(<LicensePlateInput value={value} onChange={onChange} />);
  return onChange;
}

describe('LicensePlateInput', () => {
  it('renders the German three-part plate by default with Germany checked', () => {
    setup();

    expect(screen.getByRole('radio', { name: 'Germany' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: 'Austria' })).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByLabelText('District code')).toBeInTheDocument();
    expect(screen.getByLabelText('Letters')).toBeInTheDocument();
    expect(screen.getByLabelText('Digits')).toBeInTheDocument();
    expect(screen.queryByLabelText('Licence plate')).not.toBeInTheDocument();
  });

  it('upper-cases the district, drops non-letters and caps it at 3 letters', () => {
    const onChange = setup();

    fireEvent.change(screen.getByLabelText('District code'), { target: { value: 'gö-p12x' } });

    expect(onChange).toHaveBeenLastCalledWith({ ...EMPTY_PLATE, district: 'GÖP' });
  });

  it('caps the letters at 2', () => {
    const onChange = setup();

    fireEvent.change(screen.getByLabelText('Letters'), { target: { value: 'a b c' } });

    expect(onChange).toHaveBeenLastCalledWith({ ...EMPTY_PLATE, letters: 'AB' });
  });

  it('keeps only digits and caps them at 4', () => {
    const onChange = setup();

    fireEvent.change(screen.getByLabelText('Digits'), { target: { value: '12a345' } });

    expect(onChange).toHaveBeenLastCalledWith({ ...EMPTY_PLATE, digits: '1234' });
  });

  it('shows the E suffix for electric plates', () => {
    setup({ ...EMPTY_PLATE, type: 'Electric' });

    expect(screen.getByRole('radio', { name: /electric/i })).toHaveAttribute('aria-checked', 'true');
    // Once in the type toggle, once after the digits.
    expect(screen.getAllByText('E')).toHaveLength(2);
  });

  it('switches type and country, keeping the other fields', async () => {
    const user = userEvent.setup();
    const value: PlateValue = { ...EMPTY_PLATE, district: 'GP', digits: '12' };
    const onChange = setup(value);

    await user.click(screen.getByRole('radio', { name: /historic/i }));
    expect(onChange).toHaveBeenLastCalledWith({ ...value, type: 'Historic' });

    await user.click(screen.getByRole('radio', { name: 'Austria' }));
    expect(onChange).toHaveBeenLastCalledWith({ ...value, country: 'AT' });
  });

  it('uses one free-text field for other countries, upper-cased and capped at 20', () => {
    const onChange = setup({ ...EMPTY_PLATE, country: 'OTHER' });

    expect(screen.queryByLabelText('District code')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Licence plate'), { target: { value: 'w-12345 abcdefghijklmnop' } });

    expect(onChange).toHaveBeenLastCalledWith({ ...EMPTY_PLATE, country: 'OTHER', free: 'W-12345 ABCDEFGHIJKL' });
  });
});
