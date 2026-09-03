import { expect, test, type Page } from '@playwright/test';

/**
 * Help reaches every screen, and never costs you the one you are on.
 *
 * The brief asks for a Help button and Ctrl+H on every page. Both parts matter and only one of them
 * is visible, so this checks the invisible one on a sample across the application rather than on
 * the single screen somebody remembered to try.
 *
 * The till is checked separately and hardest: `/pos` is the screen where opening help used to mean
 * navigating, and navigating away from the till tears down its RFID connection and drops the cart.
 * So the assertion there is not "help opened" but "help opened *and we are still on /pos*".
 *
 * Needs the stack running and a password:
 *   E2E_PASSWORD='…' npx playwright test help
 */

const CREDENTIALS = {
  username: process.env.E2E_USERNAME ?? 'admin@retail25.local',
  password: process.env.E2E_PASSWORD ?? '',
};

async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Username or email').fill(CREDENTIALS.username);
  await page.getByLabel('Password', { exact: true }).fill(CREDENTIALS.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => url.pathname !== '/sign-in');
}

/** One from each section of the rail, plus two child pages, plus the till. */
const ROUTES: Array<{ path: string; heading: string }> = [
  { path: '/dashboard', heading: 'Dashboard' },
  { path: '/pos', heading: 'Point of Sale' },
  { path: '/sales', heading: 'Previous sales' },
  { path: '/customers', heading: 'Customers' },
  { path: '/inventory', heading: 'Stock' },
  { path: '/inventory/counts', heading: 'Stock counts' },
  { path: '/catalog/products', heading: 'Inventory' },
  { path: '/purchasing', heading: 'Purchasing' },
  { path: '/reports', heading: 'Reports' },
  { path: '/admin/staff', heading: 'Staff' },
  { path: '/admin/backup', heading: 'Backup and restore' },
];

const panel = (page: Page) => page.getByRole('dialog').filter({ has: page.getByText('Help', { exact: true }) });

test.describe('help', () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the signed-in help checks.');

  test.beforeEach(async ({ page }) => {
    await signIn(page);
  });

  for (const route of ROUTES) {
    test(`Ctrl+H opens the guide for ${route.path} without leaving it`, async ({ page }) => {
      await page.goto(route.path);
      await page.waitForLoadState('networkidle');

      await page.keyboard.press('Control+h');

      const dialog = page.getByRole('dialog');
      await expect(dialog).toBeVisible();

      // The guide for *this* screen, not the index. A help panel that opens the wrong guide is
      // worse than one that does not open.
      await expect(dialog.getByRole('heading', { name: route.heading })).toBeVisible();

      // And it is a real guide, not the "still being written" fallback.
      await expect(dialog.getByText('This guide is still being written')).toHaveCount(0);

      // Still here. This is the assertion the till exists for.
      expect(new URL(page.url()).pathname).toBe(route.path);

      await page.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
      expect(new URL(page.url()).pathname).toBe(route.path);
    });
  }

  test('the Help button opens the same panel as the shortcut', async ({ page }) => {
    await page.goto('/customers');
    await page.getByRole('button', { name: /^Help/ }).click();

    await expect(page.getByRole('dialog').getByRole('heading', { name: 'Customers' })).toBeVisible();
    expect(new URL(page.url()).pathname).toBe('/customers');
  });

  test('the till offers Help in the key bar, and keeps the sale', async ({ page }) => {
    await page.goto('/pos');
    await page.waitForLoadState('networkidle');

    await page.getByRole('navigation', { name: 'Function keys' }).getByRole('button', { name: /Help/ }).click();

    await expect(page.getByRole('dialog').getByRole('heading', { name: 'Point of Sale' })).toBeVisible();
    expect(new URL(page.url()).pathname).toBe('/pos');
  });

  test('a guide can still be read as a page', async ({ page }) => {
    await page.goto('/help/counts');
    await expect(page.getByRole('heading', { level: 1, name: 'Stock counts' })).toBeVisible();
  });
});
