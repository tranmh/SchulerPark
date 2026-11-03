import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { apiLogin, loginAsAdmin, login, USER_FINN } from '../helpers/auth';
import { AdminApi } from '../helpers/api';

const MAILHOG_URL = process.env.MAILHOG_URL || 'http://localhost:8026';

/**
 * Main user journey (Phase 18 + 21): an external visitor registers with first name,
 * surname and company, an admin judges the request on /admin/approvals, the visitor
 * signs in and books a spot through the 4-step wizard.
 *
 * Each test uses its own location (created through the admin API) so seeded blocked
 * days and bookings left by other specs cannot interfere.
 */

/** True when MailHog answers — only then can a fresh account verify its address. */
async function mailhogAvailable(): Promise<boolean> {
  try {
    const res = await fetch(`${MAILHOG_URL}/api/v2/messages?limit=1`);
    return res.ok;
  } catch {
    return false;
  }
}

/** Poll MailHog for the verification link sent to `email` (path + query). */
async function fetchVerificationLink(email: string): Promise<string> {
  for (let attempt = 0; attempt < 20; attempt++) {
    const res = await fetch(`${MAILHOG_URL}/api/v2/search?kind=to&query=${encodeURIComponent(email)}`);
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
  throw new Error(`No verification mail for ${email} in MailHog`);
}

async function registerViaUi(
  page: Page,
  visitor: { email: string; firstName: string; lastName: string; company: string; password: string },
) {
  await page.goto('/register');
  await page.getByLabel('Email').fill(visitor.email);
  await page.getByLabel(/first name/i).fill(visitor.firstName);
  await page.getByLabel(/surname/i).fill(visitor.lastName);
  await page.getByLabel(/company/i).fill(visitor.company);
  await page.getByLabel('Password', { exact: true }).fill(visitor.password);
  await page.getByLabel(/confirm password/i).fill(visitor.password);
  await page.getByRole('button', { name: /create account/i }).click();
  await expect(page.getByRole('heading', { name: /check your email/i })).toBeVisible({ timeout: 10_000 });
}

/** Approve the pending registration of `email` on /admin/approvals (admin session on `page`). */
async function approveViaUi(page: Page, email: string, displayName: string, company: string) {
  await page.goto('/admin/approvals');
  const row = page.getByRole('row').filter({ hasText: email });
  await expect(row).toBeVisible({ timeout: 10_000 });
  await expect(row.getByText(displayName)).toBeVisible();
  await expect(row.getByText(company)).toBeVisible();

  const decided = page.waitForResponse(
    (r) => r.url().includes('/approval') && r.request().method() === 'POST' && r.ok(),
  );
  await row.getByRole('button', { name: /approve/i }).click();
  await decided;
  await expect(page.getByRole('row').filter({ hasText: email })).toHaveCount(0);
}

/** Berlin "today" from the server window plus `days`, as YYYY-MM-DD, and the months to page forward. */
async function bookableDate(request: APIRequestContext, token: string, days: number) {
  const res = await request.get('/api/bookings/window', { headers: { Authorization: `Bearer ${token}` } });
  expect(res.ok()).toBeTruthy();
  const window: { today: string; maxDate: string } = await res.json();
  const d = new Date(`${window.today}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  const date = d.toISOString().slice(0, 10);
  expect(date <= window.maxDate).toBeTruthy();
  const [ty, tm] = window.today.split('-').map(Number);
  const [y, m, day] = date.split('-').map(Number);
  return { date, day, monthsAhead: (y * 12 + m) - (ty * 12 + tm) };
}

/** Walk the booking wizard: location → date → Morning → confirm; lands on /my-bookings. */
async function bookViaWizard(page: Page, locationName: string, target: { day: number; monthsAhead: number }) {
  await page.goto('/booking');
  await expect(page.getByRole('heading', { name: 'Select a location' })).toBeVisible();
  await page.getByRole('button').filter({ hasText: locationName }).first().click();

  await expect(page.getByRole('heading', { name: `Select a date at ${locationName}` })).toBeVisible();
  for (let i = 0; i < target.monthsAhead; i++) {
    await page.getByRole('button', { name: 'Next month' }).click();
  }
  await page.getByRole('button', { name: String(target.day), exact: true }).first().click();

  await expect(page.getByRole('heading', { name: /select a time slot/i })).toBeVisible();
  await page.getByRole('button', { name: /Morning/i }).first().click();

  await expect(page.getByText('Review your booking')).toBeVisible();
  await expect(page.getByText(locationName).first()).toBeVisible();
  const created = page.waitForResponse(
    (r) => r.url().endsWith('/api/bookings') && r.request().method() === 'POST',
  );
  await page.getByRole('button', { name: /Confirm Booking/i }).click();
  const res = await created;
  expect(res.ok(), `POST /api/bookings failed: ${res.status()}`).toBeTruthy();
  await expect(page).toHaveURL(/\/my-bookings$/, { timeout: 10_000 });
}

type MyBooking = { id: string; date: string; locationId: string; timeSlot: string; status: string };

async function myBookings(request: APIRequestContext, token: string): Promise<MyBooking[]> {
  const res = await request.get('/api/bookings/my?pageSize=100', { headers: { Authorization: `Bearer ${token}` } });
  expect(res.ok()).toBeTruthy();
  return ((await res.json()) as { bookings: MyBooking[] }).bookings;
}

test.describe('Main journey: register → admin approval → login → booking', () => {
  let admin: AdminApi;
  let location: { id: string; name: string };

  test.beforeAll(async ({}, testInfo) => {
    admin = await AdminApi.create(testInfo.project.use.baseURL as string);
    const suffix = Date.now().toString(36);
    location = await admin.createLocation(`E2E-Journey-${suffix}`);
    expect(location.id).toBeTruthy();
    await admin.createSlot(location.id, `J-${suffix}-1`);
    await admin.createSlot(location.id, `J-${suffix}-2`);
  });

  test.afterAll(async () => {
    if (location?.id) await admin.deactivateLocation(location.id);
    await admin?.dispose();
  });

  test('external registration carries name + company to the approval queue; approval keeps the email gate', async ({ page, browser, request }) => {
    const suffix = Date.now().toString(36);
    const visitor = {
      email: `journey-${suffix}@external.example`,
      firstName: 'Jana',
      lastName: `Reise${suffix}`,
      company: `Reise GmbH ${suffix}`,
      password: 'Visitor1234!',
    };
    const displayName = `${visitor.firstName} ${visitor.lastName}`;

    await registerViaUi(page, visitor);

    // The pending queue holds the identity fields the admin judges by; the address is not verified yet.
    const pending = await admin.pendingUsers();
    const entry = pending.users.find((u) => u.email === visitor.email);
    expect(entry).toBeTruthy();
    expect(entry!.firstName).toBe(visitor.firstName);
    expect(entry!.lastName).toBe(visitor.lastName);
    expect(entry!.company).toBe(visitor.company);
    expect(entry!.displayName).toBe(displayName);
    expect(entry!.emailVerified).toBe(false);

    // Signing in before verification is refused with the verification message.
    await page.goto('/login');
    await page.getByLabel('Email').fill(visitor.email);
    await page.getByLabel('Password', { exact: true }).fill(visitor.password);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page.getByText(/verify your email/i).first()).toBeVisible({ timeout: 5_000 });
    await expect(page).toHaveURL(/\/login/);

    // The admin (own browser session) sees name + company and approves.
    const adminContext = await browser.newContext();
    try {
      const adminPage = await adminContext.newPage();
      await loginAsAdmin(adminPage);
      await approveViaUi(adminPage, visitor.email, displayName, visitor.company);
    } finally {
      await adminContext.close();
    }

    const after = await admin.pendingUsers();
    expect(after.users.some((u) => u.email === visitor.email)).toBe(false);

    // Approval does not replace proof of mailbox ownership.
    const loginRes = await request.post('/api/auth/login', { data: { email: visitor.email, password: visitor.password } });
    expect(loginRes.status()).toBe(403);
    expect((await loginRes.json()).code).toBe('email_not_verified');
  });

  test('approved user books a spot through the wizard, sees it in My Bookings and cancels it', async ({ page, request }) => {
    const token = await apiLogin(request, USER_FINN.email, USER_FINN.password);
    const target = await bookableDate(request, token, 7);

    await login(page, USER_FINN.email, USER_FINN.password);
    await bookViaWizard(page, location.name, target);

    const booked = (await myBookings(request, token)).find(
      (b) => b.locationId === location.id && b.date === target.date && b.timeSlot === 'Morning',
    );
    expect(booked).toBeTruthy();
    expect(booked!.status).toBe('Pending');

    // My Bookings lists it; cancel it from there.
    await page.waitForLoadState('networkidle');
    // Innermost element holding both the location and a Cancel button = the booking card
    // (ancestors come first in document order, so .last() is the card itself).
    const row = page
      .locator('div')
      .filter({ hasText: location.name })
      .filter({ has: page.getByRole('button', { name: 'Cancel', exact: true }) })
      .last();
    await expect(row).toBeVisible();
    await expect(row.getByText('Pending')).toBeVisible();
    await row.getByRole('button', { name: 'Cancel', exact: true }).click();
    const deleted = page.waitForResponse(
      (r) => r.url().includes(`/api/bookings/${booked!.id}`) && r.request().method() === 'DELETE' && r.ok(),
    );
    await page.getByRole('button', { name: /cancel booking/i }).click();
    await deleted;

    const cancelled = (await myBookings(request, token)).find((b) => b.id === booked!.id);
    expect(cancelled?.status).toBe('Cancelled');
  });

  test('full journey: register, verify, admin approves, visitor signs in, sees company read-only and books', async ({ page, browser, request }) => {
    test.skip(!(await mailhogAvailable()), `MailHog not reachable at ${MAILHOG_URL}: email verification cannot be completed`);

    const suffix = Date.now().toString(36);
    const visitor = {
      email: `full-${suffix}@external.example`,
      firstName: 'Paul',
      lastName: `Gast${suffix}`,
      company: `Gast KG ${suffix}`,
      password: 'Visitor1234!',
    };
    const displayName = `${visitor.firstName} ${visitor.lastName}`;

    await registerViaUi(page, visitor);
    await page.goto(await fetchVerificationLink(visitor.email));
    await expect(page.getByRole('heading', { name: /email verified/i })).toBeVisible({ timeout: 10_000 });

    // Verified but not yet approved: the login page explains the wait.
    await page.goto('/login');
    await page.getByLabel('Email').fill(visitor.email);
    await page.getByLabel('Password', { exact: true }).fill(visitor.password);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page.getByText(/awaiting approval/i)).toBeVisible({ timeout: 5_000 });

    const adminContext = await browser.newContext();
    try {
      const adminPage = await adminContext.newPage();
      await loginAsAdmin(adminPage);
      await approveViaUi(adminPage, visitor.email, displayName, visitor.company);
    } finally {
      await adminContext.close();
    }

    await login(page, visitor.email, visitor.password);
    await expect(page.getByText(displayName).first()).toBeVisible();

    // Profile: names editable, company fixed at registration.
    await page.goto('/profile');
    await expect(page.locator('#profile-first-name')).toHaveValue(visitor.firstName);
    await expect(page.locator('#profile-last-name')).toHaveValue(visitor.lastName);
    await expect(page.locator('#profile-company')).toHaveValue(visitor.company);
    await expect(page.locator('#profile-company')).toBeDisabled();

    const token = await apiLogin(request, visitor.email, visitor.password);
    const target = await bookableDate(request, token, 8);
    await bookViaWizard(page, location.name, target);

    const booked = (await myBookings(request, token)).find(
      (b) => b.locationId === location.id && b.date === target.date && b.timeSlot === 'Morning',
    );
    expect(booked?.status).toBe('Pending');
    await request.delete(`/api/bookings/${booked!.id}`, { headers: { Authorization: `Bearer ${token}` } });
  });
});
