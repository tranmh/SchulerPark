import { test, expect } from '@playwright/test';

const MAILHOG_URL = process.env.MAILHOG_URL || 'http://localhost:8026';

/**
 * Poll MailHog for a mail to `email` whose body contains a link matching `pattern`
 * (capture group 1 = token). MailHog returns every mail to the address, so the
 * pattern picks the right one (verification vs. password reset).
 */
async function fetchMailLink(email: string, pattern: RegExp, path: string): Promise<string> {
  for (let attempt = 0; attempt < 20; attempt++) {
    const res = await fetch(
      `${MAILHOG_URL}/api/v2/search?kind=to&query=${encodeURIComponent(email)}`
    );
    if (res.ok) {
      const data = (await res.json()) as { items: { Content: { Body: string } }[] };
      for (const item of data.items) {
        // MailHog stores quoted-printable bodies: undo soft line breaks and =3D.
        const body = item.Content.Body.replace(/=\r?\n/g, '').replace(/=3D/g, '=');
        const match = body.match(pattern);
        if (match) return `${path}?token=${match[1]}`;
      }
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`No email matching ${pattern} for ${email} in MailHog`);
}

const fetchVerificationLink = (email: string) =>
  fetchMailLink(email, /verify-email\?token=([A-Za-z0-9_-]+)/, '/verify-email');

const fetchResetLink = (email: string) =>
  fetchMailLink(email, /reset-password\?token=([A-Za-z0-9_-]+)/, '/reset-password');

test.describe('Authentication', () => {
  test('shows login page by default', async ({ page }) => {
    await page.goto('/');
    // Should redirect to login
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByText('LouisE').first()).toBeVisible();
    await expect(page.getByLabel('Email')).toBeVisible();
    await expect(page.getByLabel('Password')).toBeVisible();
  });

  test('can navigate to register page', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('link', { name: /create an account/i }).click();
    await expect(page).toHaveURL(/\/register/);
    await expect(page.getByRole('heading', { name: /create your account/i })).toBeVisible();
  });

  test('register, verify email, then sign in to dashboard', async ({ page }) => {
    const uniqueEmail = `pw-test-${Date.now()}@schuler.de`;

    await page.goto('/register');
    await page.getByLabel('Email').fill(uniqueEmail);
    await page.getByLabel(/display name/i).fill('Playwright User');
    await page.getByLabel('Password', { exact: true }).fill('Test1234!');
    await page.getByLabel(/confirm password/i).fill('Test1234!');
    await page.getByRole('button', { name: /create account/i }).click();

    // No auto-login anymore: registration shows the check-your-email screen.
    await expect(page.getByRole('heading', { name: /check your email/i })).toBeVisible({ timeout: 10000 });

    // Signing in before verification is rejected with a clear message.
    await page.goto('/login');
    await page.getByLabel('Email').fill(uniqueEmail);
    await page.getByLabel('Password').fill('Test1234!');
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page.getByText(/verify your email/i)).toBeVisible({ timeout: 5000 });

    // Complete verification via the link from MailHog, then sign in.
    const link = await fetchVerificationLink(uniqueEmail);
    await page.goto(link);
    await expect(page.getByRole('heading', { name: /email verified/i })).toBeVisible({ timeout: 10000 });

    await page.getByRole('link', { name: /back to sign in/i }).click();
    await page.getByLabel('Email').fill(uniqueEmail);
    await page.getByLabel('Password').fill('Test1234!');
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page).toHaveURL('/', { timeout: 10000 });
    await expect(page.getByText('Playwright User')).toBeVisible();
  });

  test('forgot password → reset via MailHog link → sign in with new password', async ({ page, request }) => {
    const email = `pw-reset-${Date.now()}@schuler.de`;
    const oldPassword = 'Test1234!';
    const newPassword = 'Reset-Pass-2026';

    // A verified local account, created through the API (no UI needed for setup).
    const reg = await request.post('/api/auth/register', {
      data: { email, displayName: 'Reset User', password: oldPassword },
    });
    expect(reg.ok()).toBeTruthy();
    const verifyLink = await fetchVerificationLink(email);
    const verifyToken = new URL(verifyLink, 'http://x').searchParams.get('token');
    const verify = await request.post('/api/auth/verify-email', { data: { token: verifyToken } });
    expect(verify.ok()).toBeTruthy();

    // Request the reset from the login page.
    await page.goto('/login');
    await page.getByRole('link', { name: /forgot\?/i }).click();
    await expect(page).toHaveURL(/\/forgot-password/);
    await page.getByLabel('Email').fill(email);
    await page.getByRole('button', { name: /send reset link/i }).click();
    await expect(page.getByRole('heading', { name: /check your email/i })).toBeVisible({ timeout: 10000 });

    // Follow the link from MailHog and choose a new password.
    const resetLink = await fetchResetLink(email);
    await page.goto(resetLink);
    await expect(page.getByRole('heading', { name: /choose a new password/i })).toBeVisible();
    await page.getByLabel('New password', { exact: true }).fill(newPassword);
    await page.getByLabel(/confirm new password/i).fill(newPassword);
    await page.getByRole('button', { name: /save password/i }).click();
    await expect(page.getByRole('heading', { name: /password changed/i })).toBeVisible({ timeout: 10000 });

    // The new password signs in; the old one no longer does.
    await page.getByRole('link', { name: /back to sign in/i }).click();
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password').fill(oldPassword);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page.locator('.bg-rose-50')).toBeVisible({ timeout: 5000 });

    await page.getByLabel('Password').fill(newPassword);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL('/', { timeout: 10000 });
    await expect(page.getByText('Reset User')).toBeVisible();
  });

  test('login with admin credentials', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill('admin@schulerpark.local');
    await page.getByLabel('Password').fill('Admin123!');
    await page.getByRole('button', { name: /sign in/i }).click();

    // Should redirect to dashboard and show admin sidebar
    await expect(page).toHaveURL('/', { timeout: 10000 });
    await expect(page.getByText('System Administrator')).toBeVisible();
    // Admin badge in sidebar user section
    await expect(page.locator('aside span:has-text("Admin")')).toBeVisible();
  });

  test('login with wrong password shows error', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill('admin@schulerpark.local');
    await page.getByLabel('Password').fill('WrongPass!');
    await page.getByRole('button', { name: /sign in/i }).click();

    await expect(page.locator('.bg-rose-50')).toBeVisible({ timeout: 5000 });
  });

  test('register with SSO-only domain steers to Microsoft sign-in', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('Email').fill('someone@andritz.com');

    // Inline notice appears and the submit button is disabled — these users
    // must use "Continue with Microsoft" on the login page instead.
    await expect(page.getByText(/signs in with Microsoft/i)).toBeVisible({ timeout: 5000 });
    await expect(page.getByRole('button', { name: /create account/i })).toBeDisabled();
  });

  test('register with mismatched passwords shows error', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('Email').fill('mismatch@schuler.de');
    await page.getByLabel(/display name/i).fill('Test');
    await page.getByLabel('Password', { exact: true }).fill('Test1234!');
    await page.getByLabel(/confirm password/i).fill('Different!');
    await page.getByRole('button', { name: /create account/i }).click();

    await expect(page.getByText('Passwords do not match')).toBeVisible();
  });

  test('logout returns to login page', async ({ page }) => {
    // Login first
    await page.goto('/login');
    await page.getByLabel('Email').fill('admin@schulerpark.local');
    await page.getByLabel('Password').fill('Admin123!');
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL('/', { timeout: 10000 });

    // Logout
    await page.getByRole('button', { name: /sign out/i }).click();
    await expect(page).toHaveURL(/\/login/, { timeout: 5000 });
  });
});
