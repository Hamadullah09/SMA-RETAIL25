import { expect, test } from '@playwright/test';
import { marketer } from './fixtures';
import { mockApi } from './mock-api';

test('a segment is made from rules, previewed, saved, evaluated and exported', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/segments/');
  await expect(page.getByRole('row', { name: /Big spenders/ })).toContainText('Spent at least $500.00 in the last 90 days');
  await expect(page.getByRole('row', { name: /Big spenders/ })).toContainText('42');

  await page.getByRole('button', { name: /New segment/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New segment' });
  await expect(dialog.getByTestId('segment-summary')).toHaveText('Every client');
  await dialog.getByLabel('Name', { exact: true }).fill('March birthdays on WhatsApp');
  await dialog.getByLabel('Counted over').selectOption('days');
  await dialog.getByLabel('Days', { exact: true }).fill('365');
  await dialog.getByLabel('Spent at least').fill('abc');
  await expect(dialog.getByText('The least spend is not a number.')).toBeVisible();
  await dialog.getByLabel('Spent at least').fill('250');
  await dialog.getByLabel('Birthday in').selectOption({ label: 'March' });
  await dialog.getByLabel('WhatsApp').selectOption('yes');
  await expect(dialog.getByTestId('segment-summary')).toHaveText('Spent at least $250.00 in the last 365 days · Birthday in March · Agreed to WhatsApp');

  await dialog.getByRole('button', { name: /Preview/ }).click();
  await expect(dialog.getByTestId('segment-preview')).toContainText('3 clients');
  await expect(dialog.getByTestId('segment-preview').getByRole('row', { name: /Daniel Kim/ })).toContainText('$1,840.50');
  expect(api.last('POST', '/api/segments/preview')?.body).toEqual({ rules: { periodDays: 365, minSpend: 250, birthdayMonth: 3, whatsAppConsent: true } });

  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/segments')?.body).toEqual({
    name: 'March birthdays on WhatsApp',
    description: null,
    rules: { periodDays: 365, minSpend: 250, birthdayMonth: 3, whatsAppConsent: true },
  });

  await page.getByRole('row', { name: /Big spenders/ }).click();
  await page.getByRole('button', { name: /Evaluate/ }).click();
  await expect.poll(() => api.last('POST', '/api/segments/seg-1/evaluate')?.method).toBe('POST');
  await expect(page.getByRole('heading', { name: 'Big spenders: 3 clients' })).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: /Export CSV/ }).click();
  expect((await download).suggestedFilename()).toBe('segment-Big-spenders.csv');
});

test("a marketer's segments are for their own store", async ({ page }) => {
  await mockApi(page, { user: marketer });
  await page.goto('/segments/');
  await page.getByRole('row', { name: /Big spenders/ }).click();
  // A company-wide segment is read-only for them.
  await expect(page.getByRole('button', { name: /^Open/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Delete/ })).toHaveCount(0);
  await page.getByRole('row', { name: /Lapsed Downtown/ }).click();
  await expect(page.getByRole('button', { name: /^Edit/ })).toBeVisible();

  await page.getByRole('button', { name: /New segment/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New segment' });
  await expect(dialog.getByLabel('At the store')).toBeDisabled();
  await expect(dialog.getByLabel('At the store')).toHaveValue('s-main');
  await expect(dialog.getByTestId('segment-summary')).toHaveText('At Downtown');
});
