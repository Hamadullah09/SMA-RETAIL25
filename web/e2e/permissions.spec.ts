import { expect, test } from '@playwright/test';
import { supervisor } from './fixtures';
import { mockApi } from './mock-api';

test('the navigation shows only the pages the role may open', async ({ page }) => {
  await mockApi(page, { user: supervisor });
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  await expect(nav.getByRole('link', { name: /Dashboard/ })).toBeVisible();

  for (const name of ['Dashboard', 'Tills', 'Sales Journal', 'Inventory', 'Clients', 'Suppliers', 'Stock', 'Gift cards', 'Reports', 'Packages']) {
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toBeVisible();
  }
  for (const name of ['Staff', 'Users', 'Roles', 'Stores', 'Setup', 'Audit log']) {
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toHaveCount(0);
  }

  // The home store is fixed for someone without stores.all.
  await expect(page.getByTitle('Your home store')).toHaveText(/Downtown/);
  await expect(page.locator('#store-picker')).toHaveCount(0);
});

test('a page the role lacks says so, and its keys and palette entries are gone', async ({ page }) => {
  const api = await mockApi(page, { user: supervisor });
  await page.goto('/users/');
  await expect(page.getByText('Your role cannot open this page.')).toBeVisible();
  expect(api.requests.some((r) => r.path === '/api/users')).toBe(false);

  await page.keyboard.press('Control+k');
  await page.getByRole('combobox').fill('users');
  await expect(page.getByRole('option', { name: /Go to Users/ })).toHaveCount(0);
  await page.keyboard.press('Escape');

  // Ctrl+0 is Staff, which a supervisor may not open: nothing happens.
  await page.keyboard.press('Control+0');
  await expect(page).toHaveURL(/\/users\/$/);
});

test('a 403 from the server shows "Your role cannot open this page."', async ({ page }) => {
  await mockApi(page, { overrides: { 'GET /api/audit': () => ({ status: 403, json: { error: '' } }) } });
  await page.goto('/audit/');
  await expect(page.getByText('Your role cannot open this page.')).toBeVisible();
});

test('a user without the dashboard lands on their first page', async ({ page }) => {
  await mockApi(page, { user: { ...supervisor, permissions: ['web.signin', 'inventory.view'] } });
  await page.goto('/');
  await expect(page).toHaveURL(/\/tills\/$/);
});
