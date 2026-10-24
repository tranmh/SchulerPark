import { test, expect, type Page, type Route } from '@playwright/test';
import { USER_ANNA } from '../helpers/auth';

/**
 * General feedback from the sidebar (B23, CLAUDE.md:108): POST /api/feedback is mailed to the
 * admins and answered with 202; more than 5 per 10 minutes is refused with feedback_rate_limited.
 *
 * The browser drives the real front end; /api/** is answered in-test so the flow does not
 * depend on a seeded backend or a mail server.
 */
test.use({ baseURL: process.env.AQAA_BASE_URL || process.env.BASE_URL || 'http://localhost:8080' });

const ANNA = {
  id: '00000000-0000-0000-0000-0000000000a1',
  email: USER_ANNA.email,
  displayName: 'Anna Müller',
  carLicensePlate: null,
  role: 'User',
  hasAzureAd: false,
  preferredLocationId: null,
  preferredSlotId: null,
  preferredLanguage: 'en',
  hasPassword: true,
};

/** Stubs the auth endpoints for Anna; `onFeedback` answers POST /api/feedback. */
async function stubApi(page: Page, onFeedback: (route: Route) => Promise<void>) {
  let signedIn = false;
  const auth = { accessToken: 'token-anna', expiresAt: '2099-01-01T00:00:00Z', user: ANNA };
  await page.route('**/api/**', (route) => {
    const req = route.request();
    const path = new URL(req.url()).pathname;
    const json = (status: number, body: unknown) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

    if (path === '/api/auth/config') {
      return json(200, { azureAdEnabled: false, azureAdClientId: null, azureAdTenantId: null, ssoDomains: [] });
    }
    if (path === '/api/auth/refresh') return signedIn ? json(200, auth) : json(401, { error: 'No session' });
    if (path === '/api/auth/login') {
      signedIn = true;
      return json(200, auth);
    }
    if (path === '/api/feedback' && req.method() === 'POST') return onFeedback(route);
    return json(404, { error: 'Not stubbed' });
  });
}

async function openFeedback(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(USER_ANNA.email);
  await page.getByLabel('Password', { exact: true }).fill(USER_ANNA.password);
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page).toHaveURL(/\/$/);

  await page.locator('aside').getByRole('button', { name: 'Feedback', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('heading', { name: 'Feedback' })).toBeVisible();
  return dialog;
}

test.describe('Feedback', () => {
  test('FeedbackModal submit sends general feedback and thanks the user', async ({ page }) => {
    const posted: unknown[] = [];
    await stubApi(page, async (route) => {
      posted.push(route.request().postDataJSON());
      await route.fulfill({ status: 202 });
    });

    const dialog = await openFeedback(page);
    const send = dialog.getByRole('button', { name: 'Send feedback' });

    // Nothing to send yet: whitespace alone does not enable the button.
    await expect(send).toBeDisabled();
    await dialog.getByLabel('Message').fill('   ');
    await expect(send).toBeDisabled();

    await dialog.getByLabel('Message').fill('  The barrier at the south gate is slow.  ');
    await expect(send).toBeEnabled();
    await send.click();

    await expect(dialog.getByRole('status')).toHaveText('Thank you! Your report was sent to the administrators.');
    expect(posted).toEqual([{ category: 'General', comment: 'The barrier at the south gate is slow.' }]);

    await dialog.getByRole('button', { name: 'Close' }).last().click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('FeedbackModal submit shows the rate-limit message and keeps the text', async ({ page }) => {
    await stubApi(page, (route) =>
      route.fulfill({
        status: 429,
        contentType: 'application/problem+json',
        body: JSON.stringify({ status: 429, detail: 'Too many reports.', code: 'feedback_rate_limited' }),
      })
    );

    const dialog = await openFeedback(page);
    await dialog.getByLabel('Message').fill('Sixth message in ten minutes');
    await dialog.getByRole('button', { name: 'Send feedback' }).click();

    await expect(
      dialog.getByText("You've sent several reports just now. Please try again in a few minutes.")
    ).toBeVisible();
    await expect(dialog.getByRole('status')).toHaveCount(0);
    await expect(dialog.getByLabel('Message')).toHaveValue('Sixth message in ten minutes');
    await expect(dialog.getByRole('button', { name: 'Send feedback' })).toBeEnabled();
  });
});
