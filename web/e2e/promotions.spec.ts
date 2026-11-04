import { expect, test } from '@playwright/test';
import { marketer } from './fixtures';
import { mockApi } from './mock-api';

test('promotions are listed with what they do, whether they are live and their coupon uses', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/promotions/');
  await expect(page.getByRole('row', { name: /Socks 3 for 2/ })).toContainText('Buy X get Y');
  await expect(page.getByRole('row', { name: /Socks 3 for 2/ })).toContainText('Live');
  await expect(page.getByRole('row', { name: /Weekend footwear/ })).toContainText('Not today');
  await expect(page.getByRole('row', { name: /Tee and cap/ })).toContainText('Off');
  await expect(page.getByRole('row', { name: /Spend 100 save 10/ })).toContainText('SAVE10 · 12 of 100 used');
  await expect(page.getByRole('row', { name: /Tee and cap/ })).toContainText('MAIN');

  await page.getByText('Switched on only').click();
  await expect.poll(() => api.last('GET', '/api/promotions')?.query.get('active')).toBe('true');
  await expect(page.getByRole('row', { name: /Tee and cap/ })).toHaveCount(0);
});

test('a new promotion shows only the fields of its kind and is saved with what they say', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/promotions/?new=1');
  const dialog = page.getByRole('dialog', { name: 'New promotion' });
  await expect(dialog.getByLabel('Percent off', { exact: true })).toHaveValue('10');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Give the promotion a name.')).toBeVisible();

  await dialog.getByLabel('Name', { exact: true }).fill('Socks happy hour');
  await dialog.getByLabel('Kind').selectOption('BuyXGetY');
  await expect(dialog.getByLabel('Percent off', { exact: true })).toHaveCount(0);
  await dialog.getByLabel('Buy', { exact: true }).fill('3');
  await dialog.getByLabel('What they buy').selectOption('Items');
  await dialog.getByRole('group', { name: 'Applies to' }).getByRole('button', { name: /Add a line/ }).click();
  await dialog.getByLabel('Applies to line 1', { exact: true }).fill('sk-001');
  await dialog.getByRole('button', { name: 'Friday' }).click();
  await dialog.getByRole('button', { name: 'Saturday' }).click();
  await dialog.getByLabel('From', { exact: true }).fill('17:00');
  await dialog.getByLabel('Until').fill('19:00');
  await dialog.getByLabel('Coupon code').fill('socks3');
  await dialog.getByLabel('Coupon uses (0 no limit)').fill('1');
  await expect(dialog.getByTestId('promotion-summary')).toHaveText('Buy 3 of SK-001, get 1 more free · Fri, Sat, 17:00–19:00 · every store · coupon SOCKS3, one use');

  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/promotions')?.body).toMatchObject({
    name: 'Socks happy hour',
    kind: 'BuyXGetY',
    buyQuantity: 3,
    getQuantity: 1,
    getPercent: 100,
    scope: 'Items',
    targets: [{ plu: 'SK-001', quantity: 1 }],
    days: [5, 6],
    startTime: '17:00',
    endTime: '19:00',
    couponCode: 'SOCKS3',
    couponMaxUses: 1,
    allStores: true,
    storeIds: [],
  });
});

test('a bundle needs its parts, and a saved promotion opens with its targets', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/promotions/');
  await page.getByRole('row', { name: /Weekend footwear/ }).dblclick();
  const dialog = page.getByRole('dialog', { name: 'Weekend footwear' });
  await expect(dialog.getByLabel('Applies to line 1', { exact: true })).toHaveValue('d-2');
  await expect(dialog.getByRole('button', { name: 'Saturday' })).toHaveAttribute('aria-pressed', 'true');
  await dialog.getByLabel('Kind').selectOption('BundlePrice');
  await dialog.getByLabel('Bundle price').fill('30');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('A bundle has at least two items.')).toBeVisible();
  await dialog.getByRole('group', { name: 'In the bundle' }).getByRole('button', { name: /Add a line/ }).click();
  await dialog.getByLabel('In the bundle line 1', { exact: true }).fill('TS-100-M');
  await dialog.getByLabel('In the bundle line 1 quantity').fill('2');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/promotions/p-1')?.body).toMatchObject({ kind: 'BundlePrice', bundlePrice: 30, bundleParts: [{ plu: 'TS-100-M', quantity: 2 }], version: 'v-1' });
});

test('the report says what each promotion cost and earned', async ({ page }) => {
  await mockApi(page);
  await page.goto('/promotions/');
  await page.getByRole('tab', { name: 'Report' }).click();
  await expect(page.getByRole('row', { name: /Spend 100 save 10/ })).toContainText('$1,630.20');
  await expect(page.getByRole('row', { name: /Spend 100 save 10/ })).toContainText('$120.00');
  await expect(page.locator('tfoot')).toContainText('$251.60');
});

test("a store's marketer makes promotions for their own store only", async ({ page }) => {
  await mockApi(page, { user: marketer });
  await page.goto('/promotions/');
  await page.getByRole('row', { name: /Socks 3 for 2/ }).click();
  await expect(page.getByRole('button', { name: /^Open/ })).toBeVisible();
  await page.getByRole('row', { name: /Tee and cap/ }).click();
  await expect(page.getByRole('button', { name: /^Edit/ })).toBeVisible();
  await page.getByRole('button', { name: /New promotion/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New promotion' });
  await expect(dialog.getByLabel('Every store')).not.toBeChecked();
  await expect(dialog.getByLabel('Every store')).toBeDisabled();
  await expect(dialog.getByTestId('promotion-summary')).toContainText('MAIN');
});
