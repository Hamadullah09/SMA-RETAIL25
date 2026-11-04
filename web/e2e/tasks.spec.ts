import { expect, test } from '@playwright/test';
import { crmViewer } from './fixtures';
import { mockApi } from './mock-api';

test('my tasks come overdue first, then today, later and without a date, and are ticked done', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/tasks/');
  await expect(page.getByRole('heading', { name: 'My tasks', level: 1 })).toBeVisible();
  await expect(page.getByText('1 overdue · 1 today · 4 open')).toBeVisible();

  const groups = page.locator('section h2');
  await expect(groups).toHaveText([/^Overdue/, /^Today/, /^Later/, /^No date/]);
  const overdue = page.getByRole('region', { name: /Overdue/ });
  await expect(overdue.getByTestId('task-a-1')).toContainText('Call Daniel about the uniforms');
  await expect(overdue.getByTestId('task-a-1')).toContainText('Opportunity Uniforms for Kim Design · Client Daniel Kim');
  // Someone else's activity is not on my list.
  await expect(page.getByText('Catering menu tasting')).toHaveCount(0);

  await page.getByLabel('Measure the wedding party done').click();
  await expect.poll(() => api.last('POST', '/api/activities/a-2/done')?.body).toEqual({ done: true, version: 'v-1' });
  await expect(page.getByText('Measure the wedding party is done.')).toBeVisible();

  // An activity opens in its editor.
  await page.getByRole('button', { name: /Send the gift proposal/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Send the gift proposal' });
  await expect(dialog.getByLabel('Subject')).toHaveValue('Send the gift proposal');
  await dialog.getByRole('button', { name: /Cancel/ }).click();
});

test('with nothing to do, the page says so', async ({ page }) => {
  await mockApi(page, { user: crmViewer });
  await page.goto('/tasks/');
  await expect(page.getByText('Nothing to do')).toBeVisible();
  await expect(page.getByRole('button', { name: /New activity/ })).toHaveCount(0);
});
