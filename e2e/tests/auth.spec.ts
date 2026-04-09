import { test, expect } from '@playwright/test';

test.describe('Authentication', () => {
  test('shows login page by default', async ({ page }) => {
    await page.goto('/');
    // Should redirect to login
    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByText('SchulerPark')).toBeVisible();
    await expect(page.getByLabel('Email')).toBeVisible();
    await expect(page.getByLabel('Password')).toBeVisible();
  });

  test('can navigate to register page', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('link', { name: /register/i }).click();
    await expect(page).toHaveURL(/\/register/);
    await expect(page.getByText('Create Account')).toBeVisible();
  });

  test('register a new user and land on dashboard', async ({ page }) => {
    const uniqueEmail = `pw-test-${Date.now()}@schuler.de`;

    await page.goto('/register');
    await page.getByLabel('Email').fill(uniqueEmail);
    await page.getByLabel('Display Name').fill('Playwright User');
    await page.getByLabel('Password', { exact: true }).fill('Test1234!');
    await page.getByLabel('Confirm Password').fill('Test1234!');
    await page.getByRole('button', { name: /register/i }).click();

    // Should redirect to dashboard
    await expect(page).toHaveURL('/', { timeout: 10000 });
    await expect(page.getByText('SchulerPark')).toBeVisible();
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

    await expect(page.locator('.bg-red-50')).toBeVisible({ timeout: 5000 });
  });

  test('register with mismatched passwords shows error', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('Email').fill('mismatch@schuler.de');
    await page.getByLabel('Display Name').fill('Test');
    await page.getByLabel('Password', { exact: true }).fill('Test1234!');
    await page.getByLabel('Confirm Password').fill('Different!');
    await page.getByRole('button', { name: /register/i }).click();

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
