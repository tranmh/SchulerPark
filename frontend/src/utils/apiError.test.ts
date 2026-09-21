import { describe, it, expect, beforeEach } from 'vitest';
import i18n from '../i18n';
import { describeApiError, getApiErrorCode } from './apiError';

const axiosLike = (data: unknown) => ({ response: { data } });

describe('describeApiError', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('de');
  });

  it('translates a known ProblemDetails code into the active language', () => {
    const err = axiosLike({
      detail: 'You already have a booking for this date, time slot, and location.',
      code: 'booking_duplicate',
    });
    expect(describeApiError(err, 'booking.createFailed')).toBe(
      'Sie haben für diesen Tag, dieses Zeitfenster und diesen Standort bereits eine Buchung.'
    );
  });

  it('translates auth-controller style { error, code } payloads', async () => {
    await i18n.changeLanguage('en');
    const err = axiosLike({ error: 'Invalid email or password.', code: 'invalid_credentials' });
    expect(describeApiError(err, 'auth.loginFailed')).toBe('Invalid email or password.');
    await i18n.changeLanguage('de');
    expect(describeApiError(err, 'auth.loginFailed')).toBe('E-Mail-Adresse oder Passwort ist ungültig.');
  });

  it('falls back to the server text for an unknown code', () => {
    const err = axiosLike({ detail: 'Something specific.', code: 'not_a_real_code' });
    expect(describeApiError(err, 'booking.createFailed')).toBe('Something specific.');
  });

  it('falls back to the generic key when there is no payload', () => {
    expect(describeApiError(new Error('Network error'), 'booking.createFailed')).toBe(
      i18n.t('booking.createFailed')
    );
  });

  it('exposes the raw code', () => {
    expect(getApiErrorCode(axiosLike({ code: 'pending_approval' }))).toBe('pending_approval');
    expect(getApiErrorCode(undefined)).toBeUndefined();
  });
});
