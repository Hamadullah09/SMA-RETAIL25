import { expect, test } from '@playwright/test';
import { newHubKey } from './fixtures';
import { mockApi } from './mock-api';

test("a store's RFID hub keys are listed, a new one is shown once, and one is revoked", async ({ page, baseURL }) => {
  const api = await mockApi(page);
  await page.goto('/stores/');
  const hubs = page.getByRole('region', { name: 'RFID hubs of Downtown' });
  const table = hubs.getByRole('table', { name: 'RFID hubs of Downtown' });

  // The list: name, key hint, made by, last seen, state.
  await expect(table.getByRole('row', { name: /Back office PC/ })).toContainText('rfh_7Qx2…');
  await expect(table.getByRole('row', { name: /Back office PC/ })).toContainText('192.168.1.20');
  await expect(table.getByRole('row', { name: /Back office PC/ })).toContainText('In use');
  await expect(table.getByRole('row', { name: /Old till PC/ })).toContainText('Revoked');
  await expect(table.getByRole('row', { name: /Old till PC/ }).getByRole('button', { name: /Revoke/ })).toHaveCount(0);

  // A new key: a name first, then the key this once, with the hub's command line.
  await hubs.getByRole('button', { name: /Add a hub key/ }).click();
  const add = page.getByRole('dialog', { name: 'Add a hub key' });
  await add.getByRole('button', { name: /Make the key/ }).click();
  await expect(add.getByText('Say where the hub runs, for example Back office PC.')).toBeVisible();
  await add.getByLabel('Hub name').fill('Back room PC');
  await add.getByRole('button', { name: /Make the key/ }).click();

  const shown = page.getByRole('dialog', { name: 'Key for Back room PC' });
  await expect(shown.getByTestId('hub-key')).toHaveText(newHubKey);
  await expect(shown.getByTestId('hub-command')).toHaveText(`SmaRetail.RfidHub.exe --server ${baseURL}/ --serverkey ${newHubKey}`);
  await expect(shown.getByText(/The key is not shown again/)).toBeVisible();
  const post = api.last('POST', '/api/stores/s-main/rfid-hubs');
  expect(post?.body).toEqual({ name: 'Back room PC' });
  expect(post?.headers['x-requested-with']).toBe('retailplus-web');

  // Once closed, the list knows the key by its first characters only.
  await shown.getByRole('button', { name: /Done/ }).click();
  await expect(shown).toBeHidden();
  await expect(table.getByRole('row', { name: /Back room PC/ })).toContainText('rfh_N3wK…');
  await expect(page.getByText(newHubKey)).toHaveCount(0);

  // Revoking asks first.
  await table.getByRole('button', { name: 'Revoke the key of Back office PC' }).click();
  const confirm = page.getByRole('dialog', { name: 'Revoke the key of Back office PC?' });
  await expect(confirm).toContainText('rfh_7Qx2…');
  await confirm.getByRole('button', { name: /^Revoke/ }).click();
  await expect.poll(() => api.last('POST', '/api/rfid-hubs/h-1/revoke')?.headers['x-requested-with']).toBe('retailplus-web');
  await expect(table.getByRole('row', { name: /Back office PC/ })).toContainText('Revoked');
  await expect(table.getByRole('row', { name: /Back office PC/ }).getByRole('button', { name: /Revoke/ })).toHaveCount(0);
});

test('a store without hub keys says so', async ({ page }) => {
  await mockApi(page);
  await page.goto('/stores/');
  await expect(page.getByRole('region', { name: 'RFID hubs of Westside Mall' })).toContainText('No hub keys yet. Add one for each PC that runs an RFID hub.');
});
