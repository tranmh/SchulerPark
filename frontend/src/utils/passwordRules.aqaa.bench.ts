import { bench, describe } from 'vitest';
import { passwordProblemKey, validatePassword } from './passwordRules';
import { describeApiError, getApiErrorCode } from './apiError';
import '../i18n';

// RegisterPage validates with these helpers before submitting.
let sink = 0;
const sizes = [8, 1024, 65536];

describe('RegisterPage validatePassword (weak: lowercase only, worst case scans)', () => {
  for (const n of sizes) {
    const pw = 'a'.repeat(n);
    bench(`len=${n}`, () => {
      const p = validatePassword(pw);
      sink += p ? passwordProblemKey(p).length : 0;
    });
  }
});

describe('RegisterPage validatePassword (strong: symbol at end)', () => {
  for (const n of sizes) {
    const pw = 'aA1'.repeat(Math.ceil(n / 3)).slice(0, n - 1) + '!';
    bench(`len=${n}`, () => {
      sink += validatePassword(pw) === null ? 1 : 0;
    });
  }
});

describe('RegisterPage error display (describeApiError/getApiErrorCode)', () => {
  const errors: [string, unknown][] = [
    ['plain Error', new Error('boom')],
    ['axios-like response', { isAxiosError: true, response: { status: 400, data: { code: 'EMAIL_DOMAIN_NOT_ALLOWED', message: 'nope' } } }],
    ['undefined', undefined],
  ];
  for (const [name, err] of errors) {
    bench(name, () => {
      sink += describeApiError(err, 'auth.registerFailed').length + (getApiErrorCode(err)?.length ?? 0);
    });
  }
});

export { sink };
