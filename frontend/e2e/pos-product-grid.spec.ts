import { expect, test, type Locator, type Page } from '@playwright/test';

/**
 * The till's product picker, in a real browser against the real stack.
 *
 * The handler behaviour is already covered by the integration suite. What only a browser can answer
 * is whether the two layouts actually render — whether a tile shows a picture, whether an item with
 * no picture falls back to a monogram rather than a broken image, and whether tapping one puts a line
 * on the sale.
 *
 * Needs the API, PostgreSQL and a demo catalogue. Run with:
 *   E2E_PASSWORD='…' npx playwright test pos-product-grid
 */

const CREDENTIALS = {
  username: process.env.E2E_USERNAME ?? 'admin@retail25.local',
  password: process.env.E2E_PASSWORD ?? '',
};

async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Username or email').fill(CREDENTIALS.username);
  // exact, because the field's reveal button is labelled "Show password" and a substring match
  // finds both. Playwright's strict mode then refuses the fill rather than picking one.
  await page.getByLabel('Password', { exact: true }).fill(CREDENTIALS.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.waitForURL((url) => url.pathname !== '/sign-in');
}

test.describe('the till can be worked from a product grid', () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the sign-in end-to-end tests.');

  test.beforeEach(async ({ page }) => {
    // Wide enough for the three-column layout; below 1280 the picker folds away by design.
    await page.setViewportSize({ width: 1600, height: 900 });
    await signIn(page);
    await page.goto('/pos');

    // The picker is a remembered toggle now, not something width alone decides, and it starts shut:
    // the till's default screen is the sale, and a cashier who works by barcode never wants a grid
    // taking a third of it. Every test here gets a clean profile, so there is nothing remembered and
    // it is always shut.
    //
    // Clicking the button *named* "Show items" rather than pressing Ctrl+G is deliberate: the label
    // flips to "Hide items" when the picker is open, so this can only ever open it — a blind hotkey
    // press would close it if that assumption ever stopped holding.
    await page.getByRole('button', { name: 'Show items' }).click();
  });

  /**
   * Waits for the grid to hold items, not merely to have drawn.
   *
   * The counter reads "0 of 0" from the first paint, before the query has answered, so a wait for
   * "some digits of some digits" is satisfied instantly by an empty grid. Three of these tests
   * survived that only because what they asserted next retried until the data arrived; the one that
   * read the count straight out of the element got the zero and failed on it.
   *
   * Insisting the total is non-zero is what makes this a wait for the shop rather than for the panel.
   */
  async function waitForItems(grid: Locator): Promise<number> {
    const counter = grid.getByText(/\d+ of \d+/);
    await expect(counter).toHaveText(/\d+ of [1-9]\d*/, { timeout: 15_000 });

    return Number((await counter.textContent())?.split(' of ')[1]);
  }

  test('the grid lists items and their pictures load', async ({ page }) => {
    const grid = page.getByRole('region', { name: 'Products' });
    await expect(grid).toBeVisible();

    // "12 of 340" — the count is the proof the query answered, not just that the panel drew.
    await waitForItems(grid);

    await grid.getByRole('radio', { name: 'Large thumbnails' }).click();

    const tiles = grid.getByRole('listitem');
    await expect(tiles.first()).toBeVisible();

    // A picture that 404s still occupies its box, so presence is not enough — ask the browser
    // whether the bytes decoded.
    const firstImage = grid.locator('img').first();
    await expect(firstImage).toBeVisible();
    await expect
      .poll(() => firstImage.evaluate((img: HTMLImageElement) => img.naturalWidth))
      .toBeGreaterThan(0);
  });

  test('switching to the list view lays the same items out row by row', async ({ page }) => {
    const grid = page.getByRole('region', { name: 'Products' });
    await waitForItems(grid);

    await grid.getByRole('radio', { name: 'List' }).click();
    await expect(grid.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');

    // The list view shows the stock code; the tile view does not. That is the difference under test.
    await expect(grid.getByRole('listitem').first().getByTestId('stock-code')).not.toBeEmpty();
  });

  test('searching narrows the grid', async ({ page }) => {
    const grid = page.getByRole('region', { name: 'Products' });

    const before = await waitForItems(grid);
    expect(before).toBeGreaterThan(0);

    await grid.getByRole('searchbox', { name: 'Search products' }).fill('zzzzz-no-such-item');

    await expect(grid.getByText(/Nothing matches/)).toBeVisible({ timeout: 10_000 });
  });

  test('tapping an item puts it on the sale', async ({ page }) => {
    const grid = page.getByRole('region', { name: 'Products' });
    await waitForItems(grid);

    // The list view, because a row carries the stock code and so identifies which item was tapped.
    await grid.getByRole('radio', { name: 'List' }).click();

    const first = grid.getByRole('listitem').first();

    const code = (await first.getByTestId('stock-code').textContent())?.trim();
    expect(code, 'the first row should show a stock code').toBeTruthy();

    // The name as well as the code, because the two screens identify an item differently: the picker
    // row leads with the stock code, the sale line shows what the customer is buying. Asserting the
    // code against the cart was asking it to display something it deliberately does not.
    const name = (await first.getByTestId('product-name').textContent())?.trim();
    expect(name, 'the first row should show a description').toBeTruthy();

    await first.getByRole('button').click();

    // The cart is the till's own region and is where the line has to appear for this to have worked.
    //
    // Named exactly rather than matched loosely against a class as well. The `.or()` was written to
    // survive one of the two changing, and instead matched both at once — the region and the element
    // carrying the class are nested, so Playwright refused the assertion for ambiguity rather than
    // making it. Two ways of finding the same thing is not a fallback.
    await expect(page.getByRole('region', { name: 'Sale lines' }))
      .toContainText(name!, { timeout: 15_000 });
  });
});
