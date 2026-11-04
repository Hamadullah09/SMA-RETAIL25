import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test("a campaign's page shows its counts, the recipients' sales and its messages", async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/campaigns/');
  await page.getByRole('row', { name: /Spring sale/ }).click();
  await page.getByRole('link', { name: /Results/ }).click();
  await expect(page).toHaveURL(/\/campaigns\/view\/\?id=cm-1$/);
  await expect(page.getByRole('heading', { name: 'Spring sale', level: 1 })).toBeVisible();
  // The navigation keeps Campaigns lit.
  await expect(page.locator('[data-shell="nav"]').getByRole('link', { name: /Campaigns/ })).toHaveAttribute('aria-current', 'page');

  await expect(page.getByTestId('count-Read')).toHaveText('15');
  await expect(page.getByTestId('count-Failed')).toHaveText('2');
  await expect(page.getByText('84.6% of the messages sent reached the phone.')).toBeVisible();
  await expect(page.getByTestId('result-Bought since')).toHaveText('9');
  await expect(page.getByTestId('result-Their sales')).toHaveText('$1,432.75');

  await page.getByLabel('Sales within').selectOption('30');
  await expect.poll(() => api.last('GET', '/api/campaigns/cm-1/results')?.query.get('days')).toBe('30');
  await expect(page.getByText('Within 30 days of the start')).toBeVisible();

  await expect(page.getByRole('row', { name: /Grace Adjei/ })).toContainText('The number is not on WhatsApp.');
  await page.getByLabel('Messages').selectOption('Failed');
  await expect.poll(() => api.last('GET', '/api/campaigns/cm-1/messages')?.query.get('status')).toBe('Failed');
  await expect(page.getByRole('row', { name: /Akua Owusu/ })).toHaveCount(0);
});

test('a draft is sent from its page; an unknown campaign says so', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/campaigns/view/?id=cm-2');
  await expect(page.getByRole('heading', { name: 'Points reminder', level: 1 })).toBeVisible();
  await expect(page.getByText('No messages until it is sent')).toBeVisible();
  await page.getByRole('button', { name: /^Send/ }).click();
  await page.getByRole('dialog', { name: 'Send Points reminder' }).getByRole('button', { name: /Send now/ }).click();
  await expect.poll(() => api.last('POST', '/api/campaigns/cm-2/schedule')?.body).toEqual({ sendAtUtc: null, version: 'v-1' });

  await page.goto('/campaigns/view/?id=nope');
  await expect(page.getByText('That campaign was not found.')).toBeVisible();
});
