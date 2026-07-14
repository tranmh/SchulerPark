import { test, expect } from '@playwright/test';

const MAILHOG_URL = process.env.MAILHOG_URL || 'http://localhost:8026';

/** Poll MailHog for the verification link sent to `email`. */
async function fetchVerificationLink(email: string): Promise<string> {
  for (let attempt = 0; attempt < 20; attempt++) {
    const res = await fetch(
      `${MAILHOG_URL}/api/v2/search?kind=to&query=${encodeURIComponent(email)}`
    );
    if (res.ok) {
      const data = (await res.json()) as { items: { Content: { Body: string } }[] };
      for (const item of data.items) {
        // MailHog stores quoted-printable bodies: undo soft line breaks and =3D.
        const body = item.Content.Body.replace(/=\r?\n/g, '').replace(/=3D/g, '=');
        const match = body.match(/verify-email\?token=([A-Za-z0-9_-]+)/);
        if (match) return `/verify-email?token=${match[1]}`;
      }
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`No verification email for ${email} in MailHog`);
}

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
