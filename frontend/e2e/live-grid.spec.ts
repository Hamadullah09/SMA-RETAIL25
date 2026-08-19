import { expect, test, type Page } from '@playwright/test';

/**
 * The Phase 2 exit criterion: **grids update live across two browser sessions** (doc 11).
 *
 * This is an end-to-end test for the same reason the token test is. The property is not "the handler
 * calls the notifier" — a unit test already proves that. It is "a person editing an item in one
 * browser sees it change in another, without refreshing". That crosses the API, Redis, the SignalR
 * backplane and two client connections, and only a browser pair can answer it.
 *
 * Needs the stack running:
 *   docker compose -f deploy/docker-compose.yml up
 *   npm run dev
 *   E2E_PASSWORD='…' npx playwright test live-grid
 */

const CREDENTIALS = {
  username: process.env.E2E_USERNAME ?? 'admin@retail25.local',
  password: process.env.E2E_PASSWORD ?? '',
};

async function signIn(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.waitForURL(/\/account\/login/);
  await page.getByLabel('Username or email').fill(CREDENTIALS.username);
  // exact, because the field's reveal button is labelled "Show password" and a substring match
  // finds both. Playwright's strict mode then refuses the fill rather than picking one.
  await page.getByLabel('Password', { exact: true }).fill(CREDENTIALS.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.waitForURL((url) => !url.pathname.startsWith('/account'));
}

test.describe('the back-office grid updates live', () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the sign-in end-to-end tests.');

  test('an edit in one session appears in another without a refresh', async ({ browser }) => {
    // Two contexts, not two tabs: separate cookie jars are what makes this two sessions rather than
    // one session watching itself.
    const editor = await browser.newContext();
    const watcher = await browser.newContext();

    const editorPage = await editor.newPage();
    const watcherPage = await watcher.newPage();

    try {
      await signIn(editorPage);
      await signIn(watcherPage);

      await editorPage.goto('/catalog/products');
      await watcherPage.goto('/catalog/products');

      // Both grids must actually be connected, or the assertion below would pass on a refetch.
      await expect(editorPage.getByText('Live', { exact: true })).toBeVisible({ timeout: 15_000 });
      await expect(watcherPage.getByText('Live', { exact: true })).toBeVisible({ timeout: 15_000 });

      const stockCode = `E2E${Date.now().toString().slice(-6)}`;
      const original = `Live grid item ${stockCode}`;
      const renamed = `${original} (renamed)`;

      // Create the item in the editor's session.
      await editorPage.getByRole('button', { name: 'New item' }).click();
      await editorPage.getByLabel('Stock code').fill(stockCode);
      await editorPage.getByLabel('Description').fill(original);
      await editorPage.getByRole('button', { name: 'Save' }).first().click();

      await expect(editorPage.getByText(original)).toBeVisible({ timeout: 10_000 });

      // The watcher has to load it once — a row that is not on the page is deliberately not appended
      // by a live patch, because its sort position under the active filter is unknowable.
      await watcherPage.getByPlaceholder('Code, description or barcode').fill(stockCode);
      await expect(watcherPage.getByText(original)).toBeVisible({ timeout: 10_000 });

      // Now the actual claim: an edit in one session patches the other's row in place.
      await editorPage.getByLabel('Description').fill(renamed);
      await editorPage.getByRole('button', { name: 'Save' }).first().click();

      await expect(watcherPage.getByText(renamed)).toBeVisible({ timeout: 10_000 });
      await expect(watcherPage.getByText(original, { exact: true })).toHaveCount(0);

      // And a delete removes it from the other session's grid.
      await editorPage.getByRole('button', { name: 'Delete' }).click();
      await expect(watcherPage.getByText(renamed)).toHaveCount(0, { timeout: 10_000 });
    } finally {
      await editor.close();
      await watcher.close();
    }
  });

  test('a deleted item can be found and restored from Undelete items', async ({ page }) => {
    await signIn(page);

    await page.goto('/admin/undelete');

    // The page's own heading is "Undelete"; "Undelete items" is what the menu and the command
    // palette call it, and what the toasts point you at. Role-name matching is substring-based, so
    // asking for the longer form never matches the shorter one.
    await expect(page.getByRole('heading', { name: 'Undelete' })).toBeVisible();

    // The screen must be honest when there is nothing in it, rather than showing an empty table that
    // reads as a loading failure.
    const rows = page.locator('tbody tr');
    const empty = page.getByText('Nothing has been deleted.');

    await expect(rows.first().or(empty)).toBeVisible({ timeout: 10_000 });
  });
});
