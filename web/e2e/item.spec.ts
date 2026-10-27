import { expect, test } from '@playwright/test';
import { itemDetail } from './fixtures';
import { mockApi } from './mock-api';

test('saving an item someone else changed asks to reload, and reloading brings their values', async ({ page }) => {
  let theirs = false;
  const api = await mockApi(page, {
    overrides: {
      'GET /api/items/i-1': () => ({ json: theirs ? { ...itemDetail('i-1'), version: 'v-9', price1: 21.5 } : itemDetail('i-1') }),
      'PUT /api/items/i-1': () => {
        theirs = true;
        return { status: 409, json: { error: 'Someone else changed this item. Reload it and try again.' } };
      },
    },
  });

  await page.goto('/items/');
  // One line per store that stocks the code: this is Downtown's.
  await expect(page.getByRole('row', { name: /TS-100-M/ })).toHaveCount(2);
  await page.getByRole('row', { name: /TS-100-M.*MAIN · POS/ }).dblclick();
  await expect(page).toHaveURL(/\/items\/edit\/\?id=i-1$/);
  await expect(page.getByLabel('Description', { exact: true })).toHaveValue('Cotton T-shirt, medium');

  await page.getByRole('tab', { name: 'Pricing' }).click();
  await page.getByLabel('Price 1').fill('18.95');
  await page.keyboard.press('Control+s');

  const put = api.last('PUT', '/api/items/i-1');
  expect(put?.headers['x-requested-with']).toBe('retailplus-web');
  expect(put?.body).toMatchObject({ id: 'i-1', version: 'v-1', price1: 18.95, plu: 'TS-100-M' });
  expect(put?.body).not.toHaveProperty('stock');

  const question = page.getByRole('dialog', { name: 'Someone else changed this item. Reload?' });
  await expect(question).toBeVisible();
  await question.getByRole('button', { name: /Reload/ }).click();
  await expect(question).toBeHidden();
  await expect(page.getByLabel('Price 1')).toHaveValue('21.5');
});

test('the item editor checks the boxes before saving', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/items/edit/');
  await expect(page.getByRole('heading', { name: 'New item' })).toBeVisible();
  // Items are kept per location: a new one goes to the store chosen here.
  await expect(page.locator('#item-store')).toHaveValue('s-main');
  await page.locator('#item-store').selectOption('s-west');
  await page.getByLabel('Stock code').fill('NEW-1');
  await page.getByLabel('Barcode').fill('036000291453');
  await page.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText("The barcode's check digit is not right.")).toBeVisible();
  await expect(page.getByText('A description is needed.')).toBeVisible();
  await page.getByLabel('Barcode').fill('036000291452');
  await page.getByLabel('Description', { exact: true }).fill('Linen scarf');
  await page.getByRole('button', { name: /Save/ }).click();
  // Price 1 is on the Pricing tab, which opens by itself.
  await expect(page.getByText('Price 1 is needed.')).toBeVisible();
  await page.getByLabel('Price 1').fill('-2');
  await page.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Price 1 cannot be negative.')).toBeVisible();
  await page.getByLabel('Price 1').fill('24.00');
  await page.getByRole('button', { name: /Save/ }).click();
  await expect(page).toHaveURL(/\/items\/edit\/\?id=i-new$/);
  const post = api.last('POST', '/api/items');
  expect(post?.body).toMatchObject({ plu: 'NEW-1', barcode: '036000291452', name: 'Linen scarf', price1: 24, taxable3: false, id: null, version: null });
  expect(post?.query.get('storeId')).toBe('s-west');
});
