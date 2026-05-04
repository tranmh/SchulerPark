import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const ADMIN = { email: 'admin@schulerpark.local', password: 'Admin123!' };
export const USER_ANNA = { email: 'anna.mueller@schuler.de', password: 'Test1234!' };
export const USER_FINN = { email: 'finn.werner@schuler.de', password: 'Test1234!' };

export async function login(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL('/', { timeout: 15_000 });
  // Wait until the network is idle so the refresh-token cookie is fully
  // committed and the dashboard has hydrated. Without this, a subsequent
  // hard navigation (page.goto) can race the auth init and bounce to /login.
  await page.waitForLoadState('networkidle');
}

export const loginAsAdmin = (page: Page) => login(page, ADMIN.email, ADMIN.password);
export const loginAsAnna = (page: Page) => login(page, USER_ANNA.email, USER_ANNA.password);
export const loginAsFinn = (page: Page) => login(page, USER_FINN.email, USER_FINN.password);

/** Login via the API and return the JWT token. Used for backend setup/teardown. */
export async function apiLogin(request: APIRequestContext, email: string, password: string): Promise<string> {
  const res = await request.post('/api/auth/login', { data: { email, password } });
  if (!res.ok()) throw new Error(`apiLogin failed (${res.status()}): ${await res.text()}`);
  const body = await res.json();
  return body.accessToken;
}

export async function adminToken(request: APIRequestContext): Promise<string> {
  return apiLogin(request, ADMIN.email, ADMIN.password);
}
