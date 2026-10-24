import { expect, test, type Page } from '@playwright/test';

/**
 * A barcode scanner types into whatever has focus — and that is often not the scan box.
 *
 * A shop reported that the reader worked and the barcode did not. Part of it was the server losing
 * barcode lines to concurrent tag batches, which the application tests pin. The other part only a
 * browser can show: focus came back to the scan box only when a dialog closed, so pressing any button
 * on the till left focus on it. A scanner's digits then went into the button and vanished, and its
 * trailing Enter pressed the button — on the reader's Start/Stop, every scan toggled the reader.
 *
 * Real keystrokes, through Playwright's keyboard, are the point: a synthetic event dispatched from
 * script would not carry the browser's own focus behaviour, which is exactly what failed.
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

test.describe('the till hears a scanner wherever focus is', () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the sign-in end-to-end tests.');

  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 900 });
    await signIn(page);
    await page.goto('/pos');
    await expect(page.locator('#pos-scan')).toBeVisible({ timeout: 30_000 });
  });

  test('a code typed while a key-bar button has focus lands in the scan box', async ({ page }) => {
    const find = page.getByRole('navigation', { name: 'Function keys' }).getByRole('button', { name: /Find/ });

    // Focused, not pressed: the state a cashier leaves behind after tapping any control.
    await find.focus();
    await expect(find).toBeFocused();

    await page.keyboard.type('NOSUCHCODE1');

    const scan = page.locator('#pos-scan');
    await expect(scan).toHaveValue('NOSUCHCODE1');
    await expect(scan).toBeFocused();
  });

  test("the scanner's Enter submits the scan instead of pressing the focused button", async ({ page }) => {
    const find = page.getByRole('navigation', { name: 'Function keys' }).getByRole('button', { name: /Find/ });
    await find.focus();

    await page.keyboard.type('NOSUCHCODE2');
    await page.keyboard.press('Enter');

    // The box is cleared on submit whatever the server answers, so an empty box is the proof the
    // Enter reached the form. Find opens a dialog when pressed, so no dialog is the proof it did not
    // reach the button.
    await expect(page.locator('#pos-scan')).toHaveValue('');
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  test('typing into a real field is left alone', async ({ page }) => {
    // F9 opens Find, which has its own search box. Keys aimed at it must stay in it.
    await page.keyboard.press('F9');

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    await page.keyboard.type('shirt');

    await expect(page.locator('#pos-scan')).toHaveValue('');
  });
});
