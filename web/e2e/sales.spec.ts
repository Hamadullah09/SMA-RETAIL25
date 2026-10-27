import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('opening a sale from the journal shows its lines and payments', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/sales/');
  await expect(page.getByRole('heading', { name: 'Sales Journal' })).toBeVisible();
  await expect(page.getByText('23 sales').first()).toBeVisible();
  expect(api.last('GET', '/api/sales')?.query.get('page')).toBe('1');

  // Search goes to the server.
  await page.keyboard.press('/');
  await expect(page.getByPlaceholder('Invoice, client or item code')).toBeFocused();
  await page.keyboard.type('Akua');
  await expect.poll(() => api.last('GET', '/api/sales')?.query.get('search')).toBe('Akua');
  await expect(page.getByRole('row')).toHaveCount(1 + 3 + 1); // header, Akua's three sales, totals

  await page.getByRole('row', { name: /10000041/ }).click();
  await expect(page).toHaveURL(/\/sales\/view\/\?id=sale-1$/);
  await expect(page.getByRole('heading', { name: 'Sale 10000041' })).toBeVisible();

  const lines = page.getByRole('table', { name: 'Lines' });
  await expect(lines.getByRole('row')).toHaveCount(1 + 4 + 1);
  await expect(lines.getByRole('cell', { name: 'Baseball cap' })).toBeVisible();
  await expect(lines.getByRole('cell', { name: '-$2.00' }).first()).toBeVisible();
  await expect(lines.locator('tfoot')).toContainText('$69.47');

  const payments = page.getByRole('table', { name: 'Payments' });
  await expect(payments.getByRole('cell', { name: 'Visa' })).toBeVisible();
  await expect(payments.locator('tfoot')).toContainText('$78.50');

  // Esc goes back to the journal.
  await page.keyboard.press('Escape');
  await expect(page).toHaveURL(/\/sales\/$/);
});
