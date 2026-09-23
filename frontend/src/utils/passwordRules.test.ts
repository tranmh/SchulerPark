import { describe, it, expect } from 'vitest';
import { validatePassword, passwordProblemKey } from './passwordRules';

describe('validatePassword', () => {
  it('rejects passwords shorter than 8 characters', () => {
    expect(validatePassword('Ab1!')).toBe('too_short');
    expect(validatePassword('Short1!')).toBe('too_short');
  });

  it('rejects passwords with fewer than 3 character classes', () => {
    expect(validatePassword('abcdefgh')).toBe('too_weak');
    expect(validatePassword('Abcdefgh')).toBe('too_weak');
    expect(validatePassword('12345678')).toBe('too_weak');
  });

  it('accepts passwords with at least 3 of 4 classes', () => {
    expect(validatePassword('Abcdefg1')).toBeNull();
    expect(validatePassword('abcdefg1!')).toBeNull();
    expect(validatePassword('Test1234!')).toBeNull();
  });

  it('maps problems to the auth i18n keys', () => {
    expect(passwordProblemKey('too_short')).toBe('auth.passwordTooShort');
    expect(passwordProblemKey('too_weak')).toBe('auth.passwordTooWeak');
  });
});
