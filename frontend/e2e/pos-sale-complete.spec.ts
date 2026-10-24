import { expect, test } from '@playwright/test';
import { CREDENTIALS, openTill, ringOneItem, signIn } from './support/till';

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

test.describe('paying ends on the sale-complete screen', () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the sign-in end-to-end tests.');

  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 900 });
    await signIn(page);
    await openTill(page);
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
