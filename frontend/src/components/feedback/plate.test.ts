import { describe, it, expect } from 'vitest';
import { EMPTY_PLATE, composePlate } from './plate';

describe('composePlate', () => {
  it('formats a German plate with its H/E suffix', () => {
    const v = { ...EMPTY_PLATE, district: 'GP', letters: 'AB', digits: '1234' };
    expect(composePlate(v)).toBe('GP-AB 1234');
    expect(composePlate({ ...v, type: 'Electric' })).toBe('GP-AB 1234 E');
    expect(composePlate({ ...v, type: 'Historic' })).toBe('GP-AB 1234 H');
  });

  it('returns an empty string when nothing was entered', () => {
    expect(composePlate(EMPTY_PLATE)).toBe('');
    expect(composePlate({ ...EMPTY_PLATE, country: 'AT', free: '  ' })).toBe('');
  });

  it('uses the free-text field for other countries', () => {
    expect(composePlate({ ...EMPTY_PLATE, country: 'AT', free: 'w 12345a', district: 'GP' })).toBe('W 12345A');
  });
});
