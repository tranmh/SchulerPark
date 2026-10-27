import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { LicensePlateInput } from './LicensePlateInput';
import { EMPTY_PLATE, type PlateValue } from './plate';

function setup(value: PlateValue = EMPTY_PLATE) {
  const onChange = vi.fn<(v: PlateValue) => void>();
  render(<LicensePlateInput value={value} onChange={onChange} />);
  return onChange;
}

describe('LicensePlateInput', () => {
  it('shows the German district/letters/digits fields for DE', () => {
    setup();
    expect(screen.getByLabelText('District code')).toBeInTheDocument();
    expect(screen.getByLabelText('Letters')).toBeInTheDocument();
    expect(screen.getByLabelText('Digits')).toBeInTheDocument();
    expect(screen.queryByLabelText('Licence plate')).not.toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Germany' })).toHaveAttribute('aria-checked', 'true');
  });

  it('uppercases the district, drops non-letters and caps it at 3 characters', () => {
    const onChange = setup();
    fireEvent.change(screen.getByLabelText('District code'), { target: { value: 'gp1-xyz' } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, district: 'GPX' });
  });

  it('keeps umlauts in the district', () => {
    const onChange = setup();
    fireEvent.change(screen.getByLabelText('District code'), { target: { value: 'tö' } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, district: 'TÖ' });
  });

  it('caps the letters at 2', () => {
    const onChange = setup();
    fireEvent.change(screen.getByLabelText('Letters'), { target: { value: 'ab c' } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, letters: 'AB' });
  });

  it('keeps only digits and caps them at 4', () => {
    const onChange = setup();
    fireEvent.change(screen.getByLabelText('Digits'), { target: { value: '12a345' } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, digits: '1234' });
  });

  it('yields an empty field for input without any digit', () => {
    const onChange = setup({ ...EMPTY_PLATE, digits: '1' });
    fireEvent.change(screen.getByLabelText('Digits'), { target: { value: 'abc' } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, digits: '' });
  });

  it('switches the country and keeps the other fields', () => {
    const value: PlateValue = { ...EMPTY_PLATE, district: 'GP' };
    const onChange = setup(value);
    fireEvent.click(screen.getByRole('radio', { name: 'Other country' }));
    expect(onChange).toHaveBeenCalledWith({ ...value, country: 'OTHER' });
  });

  it('shows one free-text field for non-German plates, uppercased and capped at 20', () => {
    const onChange = setup({ ...EMPTY_PLATE, country: 'AT' });
    expect(screen.queryByLabelText('District code')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Licence plate'), { target: { value: 'w-12345 abcdefghijklmnop' } });
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, country: 'AT', free: 'W-12345 ABCDEFGHIJKL' });
  });

  it('selects the plate type and shows its suffix on the plate', () => {
    const onChange = setup();
    fireEvent.click(screen.getByRole('radio', { name: /Electric/ }));
    expect(onChange).toHaveBeenCalledWith({ ...EMPTY_PLATE, type: 'Electric' });
  });

  it('marks the selected type as checked', () => {
    setup({ ...EMPTY_PLATE, type: 'Historic' });
    expect(screen.getByRole('radio', { name: /Historic/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /Standard/ })).toHaveAttribute('aria-checked', 'false');
  });
});
