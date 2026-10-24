import { test, expect, type Page, type Route } from '@playwright/test';
import { ADMIN } from '../../helpers/auth';

/**
 * Approval flow for external-domain registrations (B10, docs/plans/phase-20-gap-analysis-remediation.md:572):
 * register → verify email → sign-in blocked until approval → admin approves in the UI → user signs in.
 *
 * The browser drives the real front end; /api/** is answered by an in-test fake of the
 * auth + admin-approval contract so the flow does not depend on a seeded backend.
 */
test.use({ baseURL: process.env.AQAA_BASE_URL || process.env.BASE_URL || 'http://localhost:8080' });

interface FakeUser {
  id: string;
  email: string;
  displayName: string;
  password: string;
  role: 'User' | 'Admin' | 'SuperAdmin';
  emailVerified: boolean;
  approved: boolean;
  createdAt: string;
}

const VERIFY_TOKEN = 'verify-token-e2e';

class FakeAuthBackend {
  users: FakeUser[] = [
    {
      id: '00000000-0000-0000-0000-000000000001',
      email: ADMIN.email,
      displayName: 'System Administrator',
      password: ADMIN.password,
      role: 'Admin',
      emailVerified: true,
      approved: true,
      createdAt: '2026-01-01T08:00:00Z',
    },
  ];
  session: FakeUser | null = null;
  approvalCalls: { id: string; body: unknown }[] = [];
  private nextId = 2;

  private toDto(u: FakeUser) {
    return {
      id: u.id,
      email: u.email,
      displayName: u.displayName,
      carLicensePlate: null,
      role: u.role,
      hasAzureAd: false,
      preferredLocationId: null,
      preferredSlotId: null,
      preferredLanguage: 'en',
      hasPassword: true,
    };
  }

  private authResponse(u: FakeUser) {
    return { accessToken: `token-${u.id}`, expiresAt: '2099-01-01T00:00:00Z', user: this.toDto(u) };
  }

  private caller(route: Route): FakeUser | undefined {
    const header = route.request().headers()['authorization'] ?? '';
    return this.users.find((u) => header === `Bearer token-${u.id}`);
  }

  async handle(route: Route) {
    const req = route.request();
    const path = new URL(req.url()).pathname;
    const method = req.method();
    const json = (status: number, body: unknown) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

    if (path === '/api/auth/config' && method === 'GET') {
      return json(200, { azureAdEnabled: false, azureAdClientId: null, azureAdTenantId: null, ssoDomains: ['andritz.com'] });
    }
    if (path === '/api/auth/refresh' && method === 'POST') {
      return this.session ? json(200, this.authResponse(this.session)) : json(401, { error: 'No session' });
    }
    if (path === '/api/auth/logout' && method === 'POST') {
      this.session = null;
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/auth/register' && method === 'POST') {
      const body = req.postDataJSON() as { email: string; displayName: string; password: string };
      this.users.push({
        id: `00000000-0000-0000-0000-${String(this.nextId++).padStart(12, '0')}`,
        email: body.email,
        displayName: body.displayName,
        password: body.password,
        role: 'User',
        emailVerified: false,
        // External domain: needs an admin's approval before the first sign-in.
        approved: false,
        createdAt: '2026-10-24T08:00:00Z',
      });
      return json(200, { message: 'Registered' });
    }
    if (path === '/api/auth/verify-email' && method === 'POST') {
      const { token } = req.postDataJSON() as { token: string };
      const pending = this.users.find((u) => !u.emailVerified);
      if (token !== VERIFY_TOKEN || !pending) return json(400, { error: 'Invalid token' });
      pending.emailVerified = true;
      return json(200, { message: 'Verified' });
    }
    if (path === '/api/auth/login' && method === 'POST') {
      const { email, password } = req.postDataJSON() as { email: string; password: string };
      const u = this.users.find((x) => x.email === email && x.password === password);
      if (!u) return json(401, { error: 'Invalid email or password' });
      if (!u.emailVerified) return json(403, { error: 'Email not verified', code: 'email_not_verified' });
      if (!u.approved) return json(403, { error: 'Pending approval', code: 'pending_approval' });
      this.session = u;
      return json(200, this.authResponse(u));
    }
    if (path === '/api/admin/users/pending' && method === 'GET') {
      if (this.caller(route)?.role !== 'Admin') return json(403, { error: 'Forbidden' });
      const users = this.users
        .filter((u) => !u.approved)
        .map(({ id, email, displayName, emailVerified, createdAt }) => ({ id, email, displayName, emailVerified, createdAt }));
      return json(200, { users, totalCount: users.length });
    }
    const approval = path.match(/^\/api\/admin\/users\/([^/]+)\/approval$/);
    if (approval && method === 'POST') {
      if (this.caller(route)?.role !== 'Admin') return json(403, { error: 'Forbidden' });
      const body = req.postDataJSON() as { approve: boolean };
      this.approvalCalls.push({ id: approval[1], body });
      const u = this.users.find((x) => x.id === approval[1]);
      if (!u) return json(404, { error: 'Not found' });
      if (body.approve) u.approved = true;
      else this.users = this.users.filter((x) => x !== u);
      const { id, email, displayName, emailVerified, createdAt } = u;
      return json(200, { id, email, displayName, emailVerified, createdAt });
    }
    // Dashboard data etc.: not part of this flow; the pages show their empty states.
    return json(404, { error: 'Not stubbed' });
  }
}

async function signIn(page: Page, email: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: /sign in/i }).click();
}

test.describe('Admin user approvals', () => {
  test('register then admin approves then login', async ({ page }) => {
    const backend = new FakeAuthBackend();
    await page.route('**/api/**', (route) => backend.handle(route));

    const email = 'external.driver@partner-firm.example';
    const password = 'Extern1234!';

    // 1. The external user registers and lands on the check-your-email screen.
    await page.goto('/register');
    await page.getByLabel('Email').fill(email);
    await page.getByLabel(/display name/i).fill('External Driver');
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByLabel(/confirm password/i).fill(password);
    await page.getByRole('button', { name: /create account/i }).click();
    await expect(page.getByRole('heading', { name: /check your email/i })).toBeVisible();

    // 2. Email verification via the link.
    await page.goto(`/verify-email?token=${VERIFY_TOKEN}`);
    await expect(page.getByRole('heading', { name: /email verified/i })).toBeVisible();

    // 3. Verified but not yet approved: sign-in is refused with the pending-approval notice.
    await signIn(page, email, password);
    await expect(page.getByText(/awaiting approval by an administrator/i)).toBeVisible();
    await expect(page).toHaveURL(/\/login/);

    // 4. The admin opens User Approvals from the sidebar and approves the registration.
    await signIn(page, ADMIN.email, ADMIN.password);
    await expect(page).toHaveURL(/\/$/);
    await page.getByRole('link', { name: 'User Approvals' }).click();
    await expect(page).toHaveURL(/\/admin\/approvals/);
    await expect(page.getByRole('heading', { name: 'User Approvals' })).toBeVisible();

    const row = page.getByRole('row').filter({ hasText: email });
    await expect(row).toBeVisible();
    await expect(row.getByText('External Driver')).toBeVisible();
    await expect(row.getByText('Verified', { exact: true })).toBeVisible();

    await row.getByRole('button', { name: 'Approve' }).click();
    await expect(page.getByText('No registrations waiting for approval.')).toBeVisible();
    await expect(row).toHaveCount(0);
    expect(backend.approvalCalls).toHaveLength(1);
    expect(backend.approvalCalls[0].body).toEqual({ approve: true });

    await page.getByRole('button', { name: /sign out/i }).click();
    await expect(page).toHaveURL(/\/login/);

    // 5. Now the user can sign in and reaches the dashboard.
    await signIn(page, email, password);
    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('aside').getByText('External Driver')).toBeVisible();
    // A regular user does not get the admin navigation.
    await expect(page.getByRole('link', { name: 'User Approvals' })).toHaveCount(0);
  });
});
