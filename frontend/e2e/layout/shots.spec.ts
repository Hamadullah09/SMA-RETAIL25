import { test, type Page } from '@playwright/test';

/**
 * Screenshots of every screen, signed in, for looking at.
 *
 * Not an assertion — a way to see the application without a person having to click through
 * thirty-five routes at three widths. Run it, look at the folder, fix what looks wrong.
 *
 *   E2E_PASSWORD='…' npx playwright test shots --project=desktop
 *
 * Skipped unless SHOTS=1, so it never runs as part of the ordinary suite: it is slow and it
 * asserts nothing, and a test that cannot fail should not be able to make CI red or green.
 */
const CREDENTIALS = {
  username: process.env.E2E_USERNAME ?? 'admin@retail25.local',
  password: process.env.E2E_PASSWORD ?? '',
};

const ROUTES = [
  '/dashboard',
  '/pos',
  '/sales',
  '/orders',
  '/customers',
  '/receivables',
  '/inventory',
  '/inventory/counts',
  '/inventory/transfers',
  '/catalog/products',
  '/catalog/bulk',
  '/purchasing',
  '/purchasing/suppliers',
  '/reports',
  '/reports/sales',
  '/reports/stock-position',
  '/help',
  '/help/pos',
  '/admin',
  '/admin/settings',
  '/admin/staff',
  '/admin/audit',
  '/admin/backup',
  '/admin/rfid',
];

async function signIn(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL(/\/account\/login/);
  await page.getByLabel('Username or email').fill(CREDENTIALS.username);
  await page.getByLabel('Password', { exact: true }).fill(CREDENTIALS.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/account'));
}

test.describe('screenshots', () => {
  test.skip(process.env.SHOTS !== '1', 'Set SHOTS=1 to capture screenshots.');
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD.');

  test('every screen', async ({ page }, testInfo) => {
    test.setTimeout(300_000);
    await signIn(page);

    for (const route of ROUTES) {
      await page.goto(route);
      await page.waitForLoadState('networkidle').catch(() => undefined);
      await page.waitForTimeout(600);

      const name = route === '/' ? 'root' : route.slice(1).replace(/\//g, '-');
      await page.screenshot({
        path: `shots/${testInfo.project.name}/${name}.png`,
        fullPage: false,
      });
    }
  });
});
