import { expect, test } from '@playwright/test';
import { supervisor } from './fixtures';
import { mockApi } from './mock-api';

test("the dashboard adds the user's tasks due today and the month's targets", async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/');
  await expect(page.getByTestId('tile-Net sales')).toHaveText('$5,532.75');

  const tasks = page.getByRole('heading', { name: 'My tasks' }).locator('xpath=ancestor::section[1]');
  await expect(page.getByTestId('my-tasks-summary')).toHaveText('1 overdue · 1 today');
  await expect(tasks).toContainText('Call Daniel about the uniforms');
  await expect(tasks).toContainText('Measure the wedding party');
  await expect(tasks).not.toContainText('Send the gift proposal');
  expect(api.last('GET', '/api/activities/mine')?.query.get('due')).toBe('today');
  await tasks.getByRole('link', { name: 'All my tasks' }).click();
  await expect(page).toHaveURL(/\/tasks\/$/);

  await page.goto('/');
  await expect(page.getByTestId('target-percent')).toHaveText('50.3%');
  await expect(page.getByRole('meter', { name: "This month's target reached" })).toHaveAttribute('aria-valuenow', '50.3');
  await expect(page.getByRole('meter', { name: 'WEST reached' })).toBeVisible();
  await expect(page.getByRole('meter', { name: 'ama · MAIN reached' })).toBeVisible();
});

test('without the CRM and targets the dashboard is as before', async ({ page }) => {
  const api = await mockApi(page, { user: supervisor });
  await page.goto('/');
  await expect(page.getByTestId('tile-Net sales')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'My tasks' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Targets' })).toHaveCount(0);
  expect(api.requests.some((r) => r.path === '/api/activities/mine' || r.path === '/api/targets/progress')).toBe(false);
});

test('a month without a target says so on the dashboard', async ({ page }) => {
  await mockApi(page, {
    overrides: {
      'GET /api/targets/progress': () => ({ json: { year: 2026, month: 11, day: 4, daysInMonth: 30, target: 0, actual: 0, percent: 0, stores: [], people: [] } }),
    },
  });
  await page.goto('/');
  await expect(page.getByText('No target is set for this month.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Set one' })).toHaveAttribute('href', '/targets/?new=1');
});
