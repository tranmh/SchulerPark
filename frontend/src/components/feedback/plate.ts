import type { PlateCountry, PlateType } from '../../types/feedback';

export interface PlateValue {
  country: PlateCountry;
  type: PlateType;
  /** German plates: district (1–3 letters), letters (1–2), digits (1–4). */
  district: string;
  letters: string;
  digits: string;
  /** Every other country: one free-text field. */
  free: string;
}

export const EMPTY_PLATE: PlateValue = {
  country: 'DE',
  type: 'Standard',
  district: '',
  letters: '',
  digits: '',
  free: '',
};

export const PLATE_SUFFIX: Record<PlateType, string> = { Standard: '', Historic: 'H', Electric: 'E' };

/** The plate as one string (e.g. "GP-AB 1234 E"), or '' when nothing was entered. */
export function composePlate(v: PlateValue): string {
  if (v.country !== 'DE') return v.free.trim().toUpperCase();
  if (!v.district && !v.letters && !v.digits) return '';
  const suffix = PLATE_SUFFIX[v.type];
  return `${v.district}-${v.letters} ${v.digits}${suffix ? ` ${suffix}` : ''}`.trim();
}
