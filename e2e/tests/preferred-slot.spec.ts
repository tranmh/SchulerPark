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

async function resetPrefs(request: APIRequestContext, token: string) {
  const me = await request.get('/api/profile', {
    headers: { Authorization: `Bearer ${token}` },
  }).then((r) => r.json());
  await request.put('/api/profile', {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      displayName: me.displayName,
      carLicensePlate: me.carLicensePlate,
      preferredLocationId: null,
      preferredSlotId: null,
    },
  });
}

test.describe('Preferred parking slot', () => {
  test.beforeEach(async ({ request }) => {
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    await resetPrefs(request, token);
  });

  test('profile page: slot dropdown disabled until preferred location is set', async ({ page }) => {
    await uiLogin(page, LISA_EMAIL, LISA_PASSWORD);
    await page.goto('/profile');

    const slot = page.getByLabel('Preferred Parking Slot');
    await expect(slot).toBeVisible({ timeout: 5000 });
    await expect(slot).toBeDisabled();

    await page.getByLabel('Preferred Parking Location').selectOption({ label: 'Weingarten' });
    await expect(slot).toBeEnabled({ timeout: 5000 });
  });

  test('profile page: selecting a preferred slot persists across reloads', async ({ page }) => {
    await uiLogin(page, LISA_EMAIL, LISA_PASSWORD);
    await page.goto('/profile');

    await page.getByLabel('Preferred Parking Location').selectOption({ label: 'Weingarten' });

    const slot = page.getByLabel('Preferred Parking Slot');
    await expect(slot).toBeEnabled({ timeout: 5000 });

    // Pick the first real slot option (index 0 is the "No preference" placeholder).
    const firstSlotValue = await slot.evaluate((el) => {
      const select = el as HTMLSelectElement;
      return select.options[1]?.value ?? '';
    });
    expect(firstSlotValue).not.toEqual('');
    await slot.selectOption(firstSlotValue);

    await page.getByRole('button', { name: /save changes/i }).click();
    await expect(page.getByText(/profile updated successfully/i)).toBeVisible({ timeout: 5000 });

    await page.reload();
    await expect(page.getByLabel('Preferred Parking Slot')).toHaveValue(firstSlotValue);
  });

  test('profile page: changing preferred location clears the preferred slot', async ({ page, request }) => {
    // Preload a preferred location+slot via API so the UI renders with both set.
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    const slots = await request.get(`/api/locations/${WEINGARTEN_ID}/slots`, {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());
    const firstActive = slots.find((s: { isActive: boolean }) => s.isActive);
    expect(firstActive).toBeTruthy();
    const me = await request.get('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());
    await request.put('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        displayName: me.displayName,
        carLicensePlate: me.carLicensePlate,
        preferredLocationId: WEINGARTEN_ID,
        preferredSlotId: firstActive.id,
      },
    });

    await uiLogin(page, LISA_EMAIL, LISA_PASSWORD);
    await page.goto('/profile');

    // Change preferred location — slot select should reset to "No preference".
    await page.getByLabel('Preferred Parking Location').selectOption(GOEPPINGEN_ID);
    await expect(page.getByLabel('Preferred Parking Slot')).toHaveValue('');
  });

  test('API: rejects preferred slot without a preferred location', async ({ request }) => {
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    const slots = await request.get(`/api/locations/${WEINGARTEN_ID}/slots`, {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());
    const firstActive = slots.find((s: { isActive: boolean }) => s.isActive);

    const res = await request.put('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        displayName: 'Lisa Weber',
        carLicensePlate: null,
        preferredLocationId: null,
        preferredSlotId: firstActive.id,
      },
    });

    expect(res.status()).toBe(400);
  });

  test('API: rejects slot from a different location than the preferred one', async ({ request }) => {
    const token = await apiLogin(request, LISA_EMAIL, LISA_PASSWORD);
    const slots = await request.get(`/api/locations/${WEINGARTEN_ID}/slots`, {
      headers: { Authorization: `Bearer ${token}` },
    }).then((r) => r.json());
    const firstActive = slots.find((s: { isActive: boolean }) => s.isActive);

    const res = await request.put('/api/profile', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        displayName: 'Lisa Weber',
        carLicensePlate: null,
        preferredLocationId: GOEPPINGEN_ID,
        preferredSlotId: firstActive.id,
      },
    });

    expect(res.status()).toBe(400);
  });
});
