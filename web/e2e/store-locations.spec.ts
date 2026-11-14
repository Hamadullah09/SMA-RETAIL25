/**
 * A store's locations on the Stores page, against the mocked server: listed with the hold location marked, added with a
 * code of 1 to 3 characters and marked as the hold location, renamed (the code stays), and the only location never the
 * hold one.
 */
import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('a store lists its locations, with the one that holds failed goods marked', async ({ page }) => {
  await mockApi(page);
  await page.goto('/stores/');
  const west = page.getByRole('region', { name: 'Locations of Westside Mall' });
  await expect(west.getByRole('row', { name: /BAK.*Back room/ })).toContainText('Hold location');
  await expect(west.getByRole('row', { name: /POS.*Westside Mall/ })).not.toContainText('Hold location');
  await expect(west.getByText('Goods that fail an inspection go to BAK unless another location is chosen.')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Locations of Downtown' }).getByText('Mark one as the hold location for goods that fail an inspection.')).toBeVisible();
});

test('a location is added as the hold location, then renamed while its code stays', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/stores/');
  const downtown = page.getByRole('region', { name: 'Locations of Downtown' });
  await downtown.getByRole('button', { name: /Add a location/ }).click();

  const add = page.getByRole('dialog', { name: 'Add a location to Downtown' });
  await add.getByLabel('Code', { exact: true }).fill('pos');
  await add.getByRole('button', { name: /Save/ }).click();
  await expect(add.getByText('The store has a location POS already.')).toBeVisible();
  await add.getByLabel('Code', { exact: true }).fill('hld');
  await add.getByLabel('Name', { exact: true }).fill('Quality hold');
  await add.getByLabel('Hold location for goods that fail an inspection').check();
  await add.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('HLD is saved as the hold location of Downtown.')).toBeVisible();
  expect(api.last('POST', '/api/stores/s-main/locations')?.body).toEqual({ version: null, code: 'HLD', name: 'Quality hold', isHold: true });
  await expect(downtown.getByRole('row', { name: /HLD.*Quality hold/ })).toContainText('Hold location');

  await downtown.getByRole('button', { name: 'Change location HLD' }).click();
  const change = page.getByRole('dialog', { name: 'Change location HLD' });
  await expect(change.getByLabel('Code', { exact: true })).toBeDisabled();
  await expect(change.getByText('The code stays: the tills find the location they sell from by it.')).toBeVisible();
  await change.getByLabel('Name', { exact: true }).fill('Quarantine');
  await change.getByRole('button', { name: /Save/ }).click();
  await expect(downtown.getByRole('row', { name: /HLD.*Quarantine/ })).toBeVisible();
  expect(api.last('PUT', '/api/locations/l-s-main-hld')?.body).toEqual({ version: 'v-1', code: 'HLD', name: 'Quarantine', isHold: true });
});

test('a store with one location cannot make it the hold location', async ({ page }) => {
  await mockApi(page);
  await page.goto('/stores/');
  await page.getByRole('region', { name: 'Locations of Northgate' }).getByRole('button', { name: 'Change location POS' }).click();
  const change = page.getByRole('dialog', { name: 'Change location POS' });
  await expect(change.getByLabel('Hold location for goods that fail an inspection')).toBeDisabled();
  await expect(change.getByText('The store’s only location cannot be its hold location: add another one first.')).toBeVisible();
});
