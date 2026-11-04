import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test("targets show each store's and salesperson's month against their net sales", async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/targets/');
  const now = new Date();
  const month = now.toLocaleString('en-US', { month: 'long' });
  await expect(page.getByTestId('target-month')).toHaveText(`${month} ${now.getFullYear()}`);
  const main = page.getByRole('row').filter({ hasText: 'MAIN' }).filter({ hasText: 'The whole store' });
  await expect(main).toContainText('$60,000.00');
  await expect(main).toContainText('$41,250.00');
  await expect(page.getByRole('meter', { name: 'ama reached' })).toHaveAttribute('aria-valuenow', '80.7');
  await expect(page.getByRole('row').filter({ hasText: 'WEST' })).toContainText('22.5%');

  await page.getByRole('button', { name: 'Previous month' }).click();
  const before = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  await expect.poll(() => api.last('GET', '/api/targets')?.query.get('month')).toBe(String(before.getMonth() + 1));
  await expect(page.getByText(/^No targets for/)).toBeVisible();
  await page.getByRole('button', { name: 'Next month' }).click();

  await page.getByRole('button', { name: /New target/ }).click();
  const dialog = page.getByRole('dialog', { name: /New target for/ });
  await dialog.getByLabel('Store').selectOption({ label: 'WEST · Westside Mall' });
  await dialog.getByLabel('For').selectOption({ label: 'Efua Quaye' });
  await dialog.getByLabel('Net sales target').fill('0');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('The target is more than 0.')).toBeVisible();
  await dialog.getByLabel('Net sales target').fill('12000');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/targets')?.body).toEqual({
    storeId: 's-west',
    userId: 'u-5',
    year: now.getFullYear(),
    month: now.getMonth() + 1,
    amount: 12000,
  });

  await page.getByRole('row', { name: /ama/ }).click();
  await page.getByRole('button', { name: /^Edit/ }).click();
  const edit = page.getByRole('dialog', { name: /Target for/ });
  await expect(edit.getByLabel('For')).toBeDisabled();
  await edit.getByLabel('Net sales target').fill('16000');
  await edit.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/targets/tg-2')?.body).toEqual({ amount: 16000, version: 'v-1' });
});
