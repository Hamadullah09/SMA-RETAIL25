import { expect, type Page, type Response } from '@playwright/test';

/**
 * What every till spec has to do before it can test anything: sign in, open the till with a drawer
 * that takes cash, and put something on the sale. Shared, so a fix to one of these (there have been
 * several — see the notes below) reaches every spec that rings a sale.
 *
 * Not a spec itself: Playwright only runs files named *.spec.ts.
 */

export const CREDENTIALS = {
  username: process.env.E2E_USERNAME ?? 'admin@retail25.local',
  password: process.env.E2E_PASSWORD ?? '',
};

export async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Username or email').fill(CREDENTIALS.username);
  // exact, because the field's reveal button is labelled "Show password".
  await page.getByLabel('Password', { exact: true }).fill(CREDENTIALS.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => url.pathname !== '/sign-in');
}

/** Opens the till and waits until it is ready for a scan, with a drawer that will take cash. */
export async function openTill(page: Page): Promise<void> {
  const drawer = page.waitForResponse((response) => response.url().includes('/drawer-sessions/current'), {
    timeout: 30_000,
  });

  await page.goto('/pos');
  await expect(page.locator('#pos-scan')).toBeVisible({ timeout: 30_000 });
  await ensureDrawerOpen(page, drawer);
}

/**
 * Cash needs an open drawer. A drawer another test already opened is left as it is.
 *
 * Decided from the server's answer, not from the screen: the status bar says "closed" and the
 * "Opening float" button is live while the drawer's state is still loading, which is how a first
 * version of this tried to open a drawer that was already open.
 */
async function ensureDrawerOpen(page: Page, current: Promise<Response>): Promise<void> {
  if ((await current).status() === 200) {
    const session = (await (await current).json()) as { status?: string } | null;
    if (session?.status === 'Open') return;
  }

  await page.keyboard.press('F10');

  const drawer = page.getByRole('dialog', { name: 'Drawer' });
  await expect(drawer).toBeVisible();
  await drawer.getByRole('button', { name: /Opening float/ }).click();

  await page.getByLabel('Amount').fill('100');
  await page.keyboard.press('Enter');

  // Back on the menu, now with the float recorded.
  await expect(page.getByRole('dialog', { name: 'Drawer' }).getByRole('button', { name: /Opening float/ })).toBeDisabled();

  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
}

/**
 * One item on the sale, found by name so the test does not depend on a stock code.
 *
 * Waits for the server to have added it, not for a matching line to be on screen: a retry, or a
 * test before this one, can leave the same item on the sale already, and the payment window then
 * opened while this scan was still in flight and changed the total under it.
 */
export async function ringOneItem(page: Page, search = 'toilet tissue'): Promise<void> {
  await page.keyboard.press('F9');

  const find = page.getByRole('dialog', { name: 'Find item' });
  await find.getByPlaceholder('Stock code or name').fill(search);

  const added = page.waitForResponse(
    (response) => /\/carts\/\d+\/lines$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST',
  );

  await find.getByRole('button', { name: new RegExp(search, 'i') }).first().click();
  expect((await added).ok()).toBe(true);

  await expect(page.locator('.pos-area-cart').getByText(new RegExp(search, 'i')).first()).toBeVisible();
}

/** Pays the exact total in cash and waits for the sale-complete screen. Returns the receipt number. */
export async function payExactCash(page: Page): Promise<number> {
  await page.keyboard.press('F4');

  const payment = page.getByRole('dialog', { name: 'Payment' });
  await expect(payment).toBeVisible();
  await payment.getByRole('button', { name: 'Cash', exact: true }).click();

  const completed = page.waitForResponse(
    (response) => /\/carts\/\d+\/complete$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST',
  );

  await payment.getByRole('button', { name: /^Take / }).click();

  const response = await completed;
  expect(response.ok()).toBe(true);

  await expect(page.getByRole('dialog', { name: 'Sale complete' })).toBeVisible({ timeout: 15_000 });

  return ((await response.json()) as { transactionNumber: number }).transactionNumber;
}
