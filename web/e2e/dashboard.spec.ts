import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('the dashboard shows the figures, their change, the charts and the lists', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/');

  await expect(page.getByRole('heading', { level: 1 })).toContainText('Sara');
  await expect(page.getByTestId('tile-Net sales')).toHaveText('$5,532.75');
  await expect(page.getByTestId('tile-Transactions')).toHaveText('148');
  await expect(page.getByTestId('tile-Average sale')).toHaveText('$37.38');
  await expect(page.getByTestId('tile-Units')).toHaveText('391');
  await expect(page.getByText('+10.9%')).toBeVisible();
  await expect(page.getByText('before: $4,987.10')).toBeVisible();

  await expect(page.getByRole('img', { name: /Cash 42%, Visa 33%/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sales by hour' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sales by day' })).toBeVisible();
  await expect(page.locator('.recharts-surface')).toHaveCount(2);
  expect(await page.locator('.recharts-bar-rectangle').count()).toBe(24 + 7);

  for (const list of ['Top items', 'By store', 'By cashier', 'By till', 'Drawer variances']) {
    await expect(page.getByRole('heading', { name: list })).toBeVisible();
  }
  await expect(page.getByRole('cell', { name: 'Cotton T-shirt, medium' })).toBeVisible();
  await expect(page.getByRole('cell', { name: '-$5.00' })).toBeVisible();

  // Today first, then yesterday: the dates go to the server.
  // "Today" is the browser's (its time zone is the test's, not this machine's).
  const [today, yesterday] = await page.evaluate(() => {
    const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const d = new Date();
    const t = ymd(d);
    d.setDate(d.getDate() - 1);
    return [t, ymd(d)];
  });
  expect(api.last('GET', '/api/dashboard')?.query.get('from')).toBe(today);
  await page.getByRole('tab', { name: 'Yesterday' }).click();
  await expect.poll(() => api.last('GET', '/api/dashboard')?.query.get('from')).toBe(yesterday);
  expect(api.last('GET', '/api/dashboard')?.query.get('to')).toBe(yesterday);

  // Custom shows the date boxes.
  await page.getByRole('tab', { name: 'Custom' }).click();
  await expect(page.getByLabel('From')).toBeVisible();
});

test('the store picker adds storeId to every request', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/');
  await expect(page.getByTestId('tile-Net sales')).toBeVisible();
  expect(api.last('GET', '/api/dashboard')?.query.get('storeId')).toBeNull();

  await page.getByLabel('Store').selectOption({ label: 'WEST · Westside Mall' });
  await expect.poll(() => api.last('GET', '/api/dashboard')?.query.get('storeId')).toBe('s-west');
  await expect(page.getByText('Westside Mall: today')).toBeVisible();

  await page.getByRole('link', { name: /Tills/ }).first().click();
  await expect.poll(() => api.last('GET', '/api/tills')?.query.get('storeId')).toBe('s-west');
  await expect(page.getByTestId('till')).toHaveCount(2);
});

test('an error loading the dashboard says so, with Try again', async ({ page }) => {
  let fail = true;
  await mockApi(page, {
    overrides: {
      'GET /api/dashboard': () => (fail ? { status: 500, json: { error: 'The database is not answering.' } } : undefined),
    },
  });
  await page.goto('/');
  await expect(page.getByText('The database is not answering.')).toBeVisible();
  fail = false;
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByTestId('tile-Net sales')).toHaveText('$5,532.75');
});
