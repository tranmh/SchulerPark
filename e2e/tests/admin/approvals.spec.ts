import { test, expect } from '@playwright/test';
import { loginAsAdmin } from '../../helpers/auth';
import { AdminApi } from '../../helpers/api';

const MAILHOG_URL = process.env.MAILHOG_URL || 'http://localhost:8026';

/** Poll MailHog for the verification token sent to `email`. */
async function fetchVerificationToken(email: string): Promise<string> {
  for (let attempt = 0; attempt < 20; attempt++) {
    const res = await fetch(`${MAILHOG_URL}/api/v2/search?kind=to&query=${encodeURIComponent(email)}`);
    if (res.ok) {
      const data = (await res.json()) as { items: { Content: { Body: string } }[] };
      for (const item of data.items) {
        const body = item.Content.Body.replace(/=\r?\n/g, '').replace(/=3D/g, '=');
        const match = body.match(/verify-email\?token=([A-Za-z0-9_-]+)/);
        if (match) return match[1];
      }
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`No verification mail for ${email} in MailHog`);
}

/**
 * Phase 21: an external registration (domain that is neither auto-approved nor
 * SSO) lands in /admin/approvals with first name, surname and company so the
 * admin can judge it. After approval the user can sign in.
 */
test.describe('Admin → User approvals', () => {
  test('external user shows name + company, can be approved, then signs in', async ({ page, request }) => {
    const suffix = Date.now().toString(36);
    const email = `visitor-${suffix}@external.example`;
    const company = `Muster GmbH ${suffix}`;
    const password = 'Visitor1234!';

    // Register through the API and verify via MailHog (the admin mail is gated on verification).
    const reg = await request.post('/api/auth/register', {
      data: { email, firstName: 'Erika', lastName: 'Muster', company, password },
    });
    expect(reg.ok()).toBeTruthy();
    const token = await fetchVerificationToken(email);
    const verify = await request.post('/api/auth/verify-email', { data: { token } });
    expect(verify.ok()).toBeTruthy();

    // Before approval: 403 pending_approval on login.
    const early = await request.post('/api/auth/login', { data: { email, password } });
    expect(early.status()).toBe(403);
    expect((await early.json()).code).toBe('pending_approval');

    // The approvals page shows who is asking.
    await loginAsAdmin(page);
    await page.goto('/admin/approvals');
    const row = page.getByRole('row').filter({ hasText: email });
    await expect(row).toBeVisible({ timeout: 10_000 });
    await expect(row.getByText('Erika Muster')).toBeVisible();
    await expect(row.getByText(company)).toBeVisible();

    const decided = page.waitForResponse(
      (r) => r.url().includes('/approval') && r.request().method() === 'POST' && r.ok()
    );
    await row.getByRole('button', { name: /approve/i }).click();
    await decided;
    await expect(page.getByRole('row').filter({ hasText: email })).toHaveCount(0);

    // After approval the visitor signs in; the profile carries the company read-only.
    const login = await request.post('/api/auth/login', { data: { email, password } });
    expect(login.ok()).toBeTruthy();
    const me = await login.json();
    expect(me.user.displayName).toBe('Erika Muster');
    expect(me.user.company).toBe(company);
  });

  test('pending list API exposes identity fields and reject works', async ({ request }, testInfo) => {
    const baseURL = testInfo.project.use.baseURL as string;
    const api = await AdminApi.create(baseURL);
    try {
      const suffix = Date.now().toString(36);
      const email = `reject-${suffix}@external.example`;
      const reg = await request.post('/api/auth/register', {
        data: { email, firstName: 'Max', lastName: 'Beispiel', company: 'Beispiel AG', password: 'Visitor1234!' },
      });
      expect(reg.ok()).toBeTruthy();

      const pending = await api.pendingUsers();
      const entry = pending.users.find((u) => u.email === email);
      expect(entry).toBeTruthy();
      expect(entry!.firstName).toBe('Max');
      expect(entry!.lastName).toBe('Beispiel');
      expect(entry!.company).toBe('Beispiel AG');

      const res = await api.decideUser(entry!.id, false);
      expect(res.ok()).toBeTruthy();
      const after = await api.pendingUsers();
      expect(after.users.some((u) => u.email === email)).toBe(false);
    } finally {
      await api.dispose();
    }
  });
});
