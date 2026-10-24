import { expect, test, type Page, type Response } from '@playwright/test';

/**
 * Paying for a sale ends on a screen that offers the receipt.
 *
 * A shop asked for it in so many words: when the customer pays, go to a print-or-save screen. Before
 * this, payment closed onto an empty sale and the change owed survived as a small line in the status
 * bar. The screen has to do that without ever slowing the queue, so two things only a browser can
 * prove are pinned here: the change is shown large, and scanning the next item closes the screen and
 * lands in the scan box rather than being lost to it.
 *
 * Real keystrokes throughout, through Playwright's keyboard. The scanner case is about where a
 * browser sends keys, which a synthetic event dispatched from script would not exercise.
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
async function ringOneItem(page: Page): Promise<void> {
  await page.keyboard.press('F9');

  const find = page.getByRole('dialog', { name: 'Find item' });
  await find.getByPlaceholder('Stock code or name').fill('toilet tissue');

  const added = page.waitForResponse(
    (response) => /\/carts\/\d+\/lines$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST',
  );

  await find.getByRole('button', { name: /toilet tissue/i }).first().click();
  expect((await added).ok()).toBe(true);

  await expect(page.locator('.pos-area-cart').getByText(/toilet tissue/i).first()).toBeVisible();
}

test.describe('paying ends on the sale-complete screen', () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the sign-in end-to-end tests.');

  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 900 });
    await signIn(page);

    const drawer = page.waitForResponse((response) => response.url().includes('/drawer-sessions/current'), {
      timeout: 30_000,
    });

    await page.goto('/pos');
    await expect(page.locator('#pos-scan')).toBeVisible({ timeout: 30_000 });
    await ensureDrawerOpen(page, drawer);
    await ringOneItem(page);
  });

  test('cash over the total shows the change, then a scan starts the next sale', async ({ page }) => {
    await page.keyboard.press('F4');

    const payment = page.getByRole('dialog', { name: 'Payment' });
    await expect(payment).toBeVisible();

    // Typed without clicking: the tendered field must hold focus. It did not — the dialog moved focus
    // to its own close button, and while the tenders loaded, to nowhere, leaving it in the scan box.
    await expect(payment.getByRole('textbox')).toBeFocused();
    await page.keyboard.type('100');
    await payment.getByRole('button', { name: /^Take / }).click();

    const done = page.getByRole('dialog', { name: 'Sale complete' });
    await expect(done).toBeVisible({ timeout: 15_000 });
    await expect(done.getByText('Give the customer')).toBeVisible();
    await expect(done.getByRole('button', { name: /Print receipt/ })).toBeVisible();
    await expect(done.getByRole('button', { name: /Save as PDF/ })).toBeVisible();
    await expect(done.getByRole('button', { name: /Next customer/ })).toBeFocused();

    // The next customer's first scan. It closes the screen and arrives whole in the scan box.
    await page.keyboard.type('NOSUCHCODE4');

    await expect(done).toBeHidden();
    await expect(page.locator('#pos-scan')).toHaveValue('NOSUCHCODE4');
    await expect(page.locator('#pos-scan')).toBeFocused();
  });

  test('the receipt can be saved as a PDF from the sale-complete screen', async ({ page }) => {
    await page.keyboard.press('F4');
    await page.getByRole('dialog', { name: 'Payment' }).getByRole('button', { name: /^Take / }).click();

    const done = page.getByRole('dialog', { name: 'Sale complete' });
    await expect(done).toBeVisible({ timeout: 15_000 });

    const download = page.waitForEvent('download');
    await done.getByRole('button', { name: /Save as PDF/ }).click();

    expect((await download).suggestedFilename()).toMatch(/^receipt-\d+\.pdf$/);

    // Enter on the focused primary button moves on to an empty sale.
    await done.getByRole('button', { name: /Next customer/ }).press('Enter');
    await expect(done).toBeHidden();
  });

  test('Escape closes the payment window even while its amount box has focus', async ({ page }) => {
    await page.keyboard.press('F4');

    const payment = page.getByRole('dialog', { name: 'Payment' });
    await expect(payment).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(payment).toBeHidden();
  });
});
