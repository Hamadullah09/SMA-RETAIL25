import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('an item is changed at every store that stocks its code, after a confirmation that lists them', async ({ page }) => {
  const api = await mockApi(page);

  // A code stocked at one location only offers no such choice.
  await page.goto('/items/edit/?id=i-2');
  await expect(page.getByLabel('Description', { exact: true })).toHaveValue('Slim jeans 32');
  await expect(page.getByText(/Apply to every store/)).toHaveCount(0);

  await page.goto('/items/edit/?id=i-1');
  await expect(page.getByLabel('Description', { exact: true })).toHaveValue('Cotton T-shirt, medium');
  const choice = page.getByLabel(/Apply to every store \(2 locations stock TS-100-M\)/);
  await expect(choice).toBeVisible();

  // Without it, a plain save of this location only.
  await page.getByRole('tab', { name: 'Pricing' }).click();
  await page.getByLabel('Price 1').fill('18.95');
  await page.keyboard.press('Control+s');
  await expect.poll(() => api.last('PUT', '/api/items/i-1')).toBeTruthy();
  expect(api.last('PUT', '/api/items/i-1')?.query.get('everyStore')).toBeNull();

  // With it, a confirmation lists where the change goes and where it does not.
  await choice.check();
  await page.getByLabel('Price 1').fill('19.95');
  await page.getByRole('button', { name: /^Save/ }).first().click();
  const confirm = page.getByRole('dialog', { name: 'Save this change at 2 locations?' });
  await expect(confirm).toBeVisible();
  await expect(confirm.getByRole('list', { name: 'Locations changed' }).getByRole('listitem')).toHaveText(['MAIN · POS', 'WEST · POS']);
  await expect(confirm.getByRole('list', { name: 'Locations left alone' })).toContainText('NORTH · POS: No item with this stock code here.');
  const before = api.requests.filter((r) => r.method === 'PUT').length;
  await confirm.getByRole('button', { name: /Save at every store/ }).click();

  await expect(page.getByRole('status', { name: 'Saved at every store' })).toContainText('Saved at 2 locations: MAIN · POS, WEST · POS.');
  const put = api.last('PUT', '/api/items/i-1');
  expect(api.requests.filter((r) => r.method === 'PUT').length).toBe(before + 1);
  expect(put?.query.get('everyStore')).toBe('true');
  expect(put?.headers['x-requested-with']).toBe('retailplus-web');
  expect(put?.body).toMatchObject({ id: 'i-1', price1: 19.95 });
  // The choice is off again after the save, so the next save is this location's only.
  await expect(choice).not.toBeChecked();
});
