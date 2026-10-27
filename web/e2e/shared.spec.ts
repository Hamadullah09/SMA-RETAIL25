import { expect, test } from '@playwright/test';
import { storeAdmin, supervisor } from './fixtures';
import { mockApi } from './mock-api';

test('every role sees the currency symbol and can filter by staff', async ({ page }) => {
  const api = await mockApi(page, { user: supervisor });
  await page.goto('/sales/');
  await expect(page.getByRole('heading', { name: 'Sales Journal' })).toBeVisible();

  // The symbol comes with the store list, not from the settings a supervisor may not read.
  await expect(page.getByRole('row', { name: /10000041/ })).toContainText('$20.34');
  expect(api.requests.some((r) => r.path.endsWith('/settings'))).toBe(false);

  const staff = page.locator('#sales-staff');
  await expect(staff.locator('option')).toHaveText(['Everyone', 'Ama Darko', 'Efua Quaye', 'Kofi Boateng', 'Sara Mensah', 'yaw']);
  expect(api.last('GET', '/api/staff')?.query.get('storeId')).toBe('s-main');
  await staff.selectOption({ label: 'Ama Darko' });
  await expect.poll(() => api.last('GET', '/api/sales')?.query.get('userId')).toBe('u-3');
});

test('a store administrator does not change the lists every store shares', async ({ page }) => {
  await mockApi(page, { user: storeAdmin });

  await page.goto('/clients/');
  await expect(page.getByRole('row', { name: /Akua Owusu/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /New client/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Delete/ })).toHaveCount(0);
  await expect(page.getByText('Clients are shared by every store: a super administrator changes them.')).toBeVisible();

  await page.goto('/suppliers/');
  await expect(page.getByRole('row', { name: /Cotton Co. Ltd/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /New supplier/ })).toHaveCount(0);

  await page.goto('/roles/');
  await expect(page.getByRole('button', { name: /New role/ })).toBeDisabled();
  await expect(page.getByRole('button', { name: /Save role/ })).toBeDisabled();
  await expect(page.getByLabel('Description')).toBeDisabled();

  await page.goto('/staff/');
  await page.getByRole('tab', { name: 'Commission rules' }).click();
  await expect(page.getByRole('row', { name: /Footwear push/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /New rule/ })).toHaveCount(0);

  // Their own store's items they still change; the shared departments and categories they do not.
  await page.goto('/items/');
  await expect(page.getByRole('link', { name: /New item/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /Departments and categories/ })).toHaveCount(0);
});

test('departments and categories are added and deleted from Inventory', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/items/');
  await page.getByRole('button', { name: /Departments and categories/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Departments and categories' });
  await expect(dialog.getByText('Footwear')).toBeVisible();

  await dialog.getByLabel('New department').fill('Scarves');
  await dialog.getByLabel('New department').press('Enter');
  await expect.poll(() => api.last('POST', '/api/departments')?.body).toEqual({ name: 'Scarves' });
  expect(api.last('POST', '/api/departments')?.headers['x-requested-with']).toBe('retailplus-web');

  await dialog.getByLabel('New category').fill('tops');
  await dialog.getByRole('button', { name: 'Add' }).nth(1).click();
  await expect(dialog.getByText('There is a category TOPS already.')).toBeVisible();

  await dialog.getByRole('button', { name: 'Delete Footwear' }).click();
  await page.getByRole('dialog', { name: /Delete the department Footwear/ }).getByRole('button', { name: /Delete/ }).click();
  await expect.poll(() => api.last('DELETE', '/api/departments/d-2')).toBeTruthy();
});

test('the audit log asks for business days like every other list', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/audit/');
  await expect(page.getByRole('heading', { name: 'Audit log' })).toBeVisible();
  const [from, to] = await page.evaluate(() => {
    const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const today = new Date();
    const monday = new Date(today);
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7));
    return [ymd(monday), ymd(today)];
  });
  await expect.poll(() => api.last('GET', '/api/audit')?.query.get('from')).toBe(from);
  expect(api.last('GET', '/api/audit')?.query.get('to')).toBe(to);
  await expect(page.locator('#audit-user option')).toHaveCount(6);
});
