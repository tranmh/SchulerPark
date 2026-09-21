import { test, expect, type Page } from '@playwright/test';
import { loginAsAdmin, loginAsAnna } from '../../helpers/auth';

/**
 * Mobile smoke suite (runs only in the `mobile-chrome` project, Pixel 7 emulation).
 * Guards the phone layout: no horizontal page overflow, drawer navigation works,
 * admin tables scroll inside their card instead of widening the page.
 */

async function expectNoHorizontalOverflow(page: Page) {
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(scrollWidth, 'page must not scroll horizontally').toBeLessThanOrEqual(innerWidth);
}

const menuButton = (page: Page) => page.getByRole('button', { name: /open menu/i });
const drawer = (page: Page) => page.locator('#app-sidebar');

test.describe('Mobile layout', () => {
  test('login page fits the viewport', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByLabel('Email')).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });

  test('sidebar is a drawer: hidden by default, opened via hamburger, closes on navigation', async ({ page }) => {
    await loginAsAnna(page);
    await expectNoHorizontalOverflow(page);

    // Closed: nav links are off-canvas / inert, hamburger is visible.
    await expect(menuButton(page)).toBeVisible();
    await expect(menuButton(page)).toHaveAttribute('aria-expanded', 'false');
    await expect(drawer(page)).toHaveAttribute('inert', '');
    // Off-canvas via translate: still "visible" to Playwright, so check the viewport instead.
    await expect(drawer(page)).not.toBeInViewport();

    // Open and navigate.
    await menuButton(page).click();
    await expect(menuButton(page)).toHaveAttribute('aria-expanded', 'true');
    await expect(drawer(page)).toBeInViewport();
    await page.getByRole('link', { name: /my bookings/i }).click();

    await expect(page).toHaveURL('/my-bookings');
    await expect(menuButton(page)).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByRole('heading', { name: /my bookings/i })).toBeVisible();
    await expectNoHorizontalOverflow(page);

    // Status filter is a horizontally scrolling strip on phones, not a wrapped block.
    const strip = page.getByTestId('status-filter');
    const { scrollWidth, clientWidth } = await strip.evaluate((el) => ({
      scrollWidth: el.scrollWidth,
      clientWidth: el.clientWidth,
    }));
    expect(scrollWidth).toBeGreaterThan(clientWidth);
  });

  test('drawer closes on backdrop tap and Escape', async ({ page }) => {
    await loginAsAnna(page);

    await menuButton(page).click();
    await page.keyboard.press('Escape');
    await expect(menuButton(page)).toHaveAttribute('aria-expanded', 'false');

    await menuButton(page).click();
    // Tap the backdrop to the right of the 256px drawer.
    await page.mouse.click(380, 500);
    await expect(menuButton(page)).toHaveAttribute('aria-expanded', 'false');
  });

  test('booking wizard fits the viewport and shows the compact stepper', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/booking');
    await expect(page.getByText(/step \d of 4/i)).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });

  test('profile page fits the viewport', async ({ page }) => {
    await loginAsAnna(page);
    await page.goto('/profile');
    await expect(page.getByRole('button', { name: /delete account|delete my account/i }).first()).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });

  test('admin table scrolls inside its card, not the page', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/bookings');
    const table = page.locator('table');
    await expect(table).toBeVisible();
    await expectNoHorizontalOverflow(page);

    const wrapper = table.locator('xpath=..');
    const { scrollWidth, clientWidth, overflowX } = await wrapper.evaluate((el) => ({
      scrollWidth: el.scrollWidth,
      clientWidth: el.clientWidth,
      overflowX: getComputedStyle(el).overflowX,
    }));
    expect(overflowX).toBe('auto');
    expect(scrollWidth).toBeGreaterThan(clientWidth);
  });
});
