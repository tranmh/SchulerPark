import { test, expect, type Page, type APIRequestContext } from '@playwright/test';

const LISA_EMAIL = 'lisa.weber@schuler.de';
const LISA_PASSWORD = 'Test1234!';
const GOEPPINGEN_ID = 'b0000000-0000-0000-0000-000000000001';
const WEINGARTEN_ID = 'b0000000-0000-0000-0000-000000000005';

async function uiLogin(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL('/', { timeout: 10000 });
}

async function apiLogin(request: APIRequestContext, email: string, password: string): Promise<string> {
  const res = await request.post('/api/auth/login', { data: { email, password } });
  expect(res.status(), await res.text()).toBe(200);
  const body = await res.json();
  return body.accessToken as string;
}

function todayPlus(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

test.describe('Preferred parking location', () => {
  test.beforeEach(async ({ request }) => {
    // Reset Lisa's preference to null so each test starts from a known state.
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    const me = await request.get('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());
    await request.put('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        displayName: me.displayName,
        carLicensePlate: me.carLicensePlate,
        preferredLocationId: null,
      },
    });
  });

  test('profile page exposes "Preferred Parking Location" dropdown and persists selection', async ({ page }) => {
    await uiLogin(page, LISA_EMAIL, LISA_PASSWORD);
    await page.goto('/profile');

    const select = page.getByLabel('Preferred Parking Location');
    await expect(select).toBeVisible({ timeout: 5000 });
    await expect(select).toHaveValue('');

    await select.selectOption({ label: 'Weingarten' });
    await page.getByRole('button', { name: /save changes/i }).click();
    await expect(page.getByText(/profile updated successfully/i)).toBeVisible({ timeout: 5000 });

    // Reload and verify the choice round-tripped from the server.
    await page.reload();
    await expect(page.getByLabel('Preferred Parking Location')).toHaveValue(WEINGARTEN_ID);
  });

  test('booking page auto-preselects the preferred location and skips step 1', async ({ page, request }) => {
    // Arrange: set preference to Weingarten via API.
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    const me = await request.get('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());
    await request.put('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        displayName: me.displayName,
        carLicensePlate: me.carLicensePlate,
        preferredLocationId: WEINGARTEN_ID,
      },
    });

    await uiLogin(page, LISA_EMAIL, LISA_PASSWORD);
    await page.goto('/booking');

    // Step 2 is the date picker with the location name in its header.
    await expect(page.getByRole('heading', { name: /select a date at weingarten/i }))
      .toBeVisible({ timeout: 10000 });
    // Step 1 "Select a location" copy should NOT be visible.
    await expect(page.getByText('Select a location')).toBeHidden();
  });

  test('API fallback: booking with preferred=Goeppingen on a Goeppingen-blocked date assigns another location', async ({ request }) => {
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    const me = await request.get('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());

    // Set preference to Goeppingen (seed blocks Goeppingen location-wide on today+3).
    await request.put('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        displayName: me.displayName,
        carLicensePlate: me.carLicensePlate,
        preferredLocationId: GOEPPINGEN_ID,
      },
    });

    const res = await request.post('/api/bookings', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        locationId: null,
        date: todayPlus(3),
        timeSlot: 'Morning',
      },
    });

    expect(res.status(), await res.text()).toBe(201);
    const booking = await res.json();

    expect(booking.fallbackReason).toBeTruthy();
    expect(booking.fallbackReason).toMatch(/goeppingen/i);
    expect(booking.locationName).not.toEqual('Goeppingen');

    // Cleanup: cancel the booking so re-runs don't hit the duplicate guard.
    await request.delete(`/api/bookings/${booking.id}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
  });

  test('API: explicit locationId bypasses fallback (honors exact client choice)', async ({ request }) => {
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);

    // Explicit POST to Goeppingen on a Goeppingen-blocked date should fail, NOT fall back.
    const res = await request.post('/api/bookings', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        locationId: GOEPPINGEN_ID,
        date: todayPlus(3),
        timeSlot: 'Morning',
      },
    });

    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(JSON.stringify(body).toLowerCase()).toContain('blocked');
  });

  test('API: user without a preference and no locationId gets a clear 400', async ({ request }) => {
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);

    const res = await request.post('/api/bookings', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        locationId: null,
        date: todayPlus(2),
        timeSlot: 'Afternoon',
      },
    });

    expect(res.status()).toBe(400);
    const body = await res.json();
    expect(JSON.stringify(body).toLowerCase()).toContain('preferred location');
  });
});
