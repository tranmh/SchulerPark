import { expect, test, type Page } from '@playwright/test';

/**
 * Registration → email verification → login (t19).
 *
 * The backend stores only a hash of the verification token and mails the raw one.
 * This run has no mail catcher (MailHog is not among the run's services), so the
 * happy path through a *valid* link and the expired-token case cannot be driven
 * here; the repository's own e2e/tests/auth.spec.ts covers those against MailHog.
 * What is verified end to end: registration lands on the check-your-email screen,
 * an unverified account cannot sign in (and can re-request the mail), and the
 * verify page handles a missing and an invalid token as specified (B50).
 */

// Registering an address that already has an *unverified* account simply rotates
// its token (AuthService.RegisterAsync), so a fixed address is safe to reuse run
// after run — the account never becomes verified because the mail is never read.
const UNVERIFIED = {
  email: 'aqaa-e2e-unverified@schuler.de',
  firstName: 'Aqaa',
  lastName: 'Unverified',
  company: 'AQAA GmbH',
  password: 'Test1234!',
};

/** Counts calls to the single-use verify endpoint made by the page. */
function trackVerifyCalls(page: Page): () => number {
  let calls = 0;
  page.on('request', (req) => {
    if (req.method() === 'POST' && req.url().includes('/api/auth/verify-email')) calls++;
  });
  return () => calls;
}

async function signIn(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
}

test.describe('Register, verify email and login (t19)', () => {
  test('registration asks for email verification and blocks sign-in until verified', async ({ page }) => {
    await page.goto('/register');
    await expect(page.getByRole('heading', { name: /create your account/i })).toBeVisible();

    await page.getByLabel('Email').fill(UNVERIFIED.email);
    await page.getByLabel(/first name/i).fill(UNVERIFIED.firstName);
    await page.getByLabel(/surname/i).fill(UNVERIFIED.lastName);
    await page.getByLabel(/company/i).fill(UNVERIFIED.company);
    await page.getByLabel('Password', { exact: true }).fill(UNVERIFIED.password);
    await page.getByLabel(/confirm password/i).fill(UNVERIFIED.password);

    const registerResponse = page.waitForResponse(
      (r) => r.url().includes('/api/auth/register') && r.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /create account/i }).click();
    expect((await registerResponse).ok()).toBe(true);

    // No auto-login: the account must confirm the address first.
    await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(page.getByText(UNVERIFIED.email)).toBeVisible();
    await expect(page).toHaveURL(/\/register$/);

    await page.getByRole('link', { name: /back to sign in/i }).click();
    await expect(page).toHaveURL(/\/login$/);

    // Signing in before verification is refused with the dedicated message …
    await signIn(page, UNVERIFIED.email, UNVERIFIED.password);
    await expect(page.getByText('Please verify your email address before signing in.')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);

    // … and the user can ask for a new mail from right there.
    const resendResponse = page.waitForResponse(
      (r) => r.url().includes('/api/auth/resend-verification') && r.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /resend verification email/i }).click();
    expect((await resendResponse).ok()).toBe(true);
    await expect(
      page.getByText('If the address has an unverified account, a new verification email has been sent.'),
    ).toBeVisible();
  });

  test('verify page without a token fails immediately and never calls the API (B50)', async ({ page }) => {
    const verifyCalls = trackVerifyCalls(page);

    await page.goto('/verify-email');
    await expect(page.getByRole('heading', { name: 'Verification failed' })).toBeVisible();
    await expect(
      page.getByText('The verification link is invalid or has expired. Request a new one from the sign-in page.'),
    ).toBeVisible();
    await page.waitForLoadState('networkidle');
    expect(verifyCalls()).toBe(0);

    await page.getByRole('link', { name: /back to sign in/i }).click();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible();
  });

  test('verify page with an invalid token reports failure after exactly one API call (B50)', async ({ page }) => {
    const verifyCalls = trackVerifyCalls(page);

    const verifyResponse = page.waitForResponse(
      (r) => r.url().includes('/api/auth/verify-email') && r.request().method() === 'POST',
    );
    await page.goto('/verify-email?token=aqaa-not-a-real-token');
    expect((await verifyResponse).status()).toBe(400);

    await expect(page.getByRole('heading', { name: 'Verification failed' })).toBeVisible();
    await expect(page.getByRole('link', { name: /back to sign in/i })).toBeVisible();

    // The token is single-use: StrictMode's double effect must not fire a second
    // request, which would turn a valid link into a false failure.
    await page.waitForLoadState('networkidle');
    expect(verifyCalls()).toBe(1);

    // An invalid link does not grant access: the account is still unverified.
    await signIn(page, UNVERIFIED.email, UNVERIFIED.password);
    await expect(page.getByText('Please verify your email address before signing in.')).toBeVisible();
  });

  test('a verified account signs in and reaches the dashboard', async ({ page }) => {
    // The seeded admin is verified (SeedData); this is the tail of the journey the
    // unverified account above cannot complete without the mailed link.
    await signIn(page, 'admin@schulerpark.local', 'Admin123!');
    await expect(page).toHaveURL('/', { timeout: 15_000 });
    await expect(page.getByText('System Administrator')).toBeVisible();
  });
});
