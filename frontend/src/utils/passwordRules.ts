/**
 * Client-side mirror of the backend PasswordPolicy: at least 8 characters and at
 * least 3 of 4 character classes. Shared by register, reset and change-password so
 * the rule cannot drift between forms. The server stays authoritative (it also
 * rejects common passwords and site terms).
 */
export const PASSWORD_MIN_LENGTH = 8;

export type PasswordProblem = 'too_short' | 'too_weak';

export function validatePassword(password: string): PasswordProblem | null {
  if (password.length < PASSWORD_MIN_LENGTH) return 'too_short';

  const classes =
    Number(/[a-z]/.test(password)) +
    Number(/[A-Z]/.test(password)) +
    Number(/[0-9]/.test(password)) +
    Number(/[^a-zA-Z0-9]/.test(password));
  if (classes < 3) return 'too_weak';

  return null;
}

/** i18n key for a validation problem (both live under `auth.*`). */
export function passwordProblemKey(problem: PasswordProblem): string {
  return problem === 'too_short' ? 'auth.passwordTooShort' : 'auth.passwordTooWeak';
}
