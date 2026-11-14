/** The user form: a user of no store needs a role that sees every store, as the server says; an e-mail to sign in with. */
import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('a user works at a store unless their role sees every store', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/users/');
  await page.getByRole('button', { name: /Add user/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Add user' });
  await dialog.getByLabel('User name').fill('mona');
  await dialog.getByLabel('Password').fill('Mona-pass-1');
  await dialog.getByLabel('Role').selectOption({ label: 'Manager' });

  // A manager sees their own store: Every store cannot be chosen, and saving without a store says why.
  const store = dialog.getByLabel('Home store');
  await expect(store.locator('option[value=""]')).toBeDisabled();
  await expect(dialog.getByText('Required: this role sees only the store the user works at.')).toBeVisible();
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Choose the store this user works at; only a role that sees every store works without one.')).toBeVisible();
  expect(api.last('POST', '/api/users')).toBeUndefined();

  // Marketing sees every store, so it works without one.
  await dialog.getByLabel('Role').selectOption({ label: 'Marketing' });
  await expect(store.locator('option[value=""]')).toBeEnabled();
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('mona is saved.')).toBeVisible();
  expect(api.last('POST', '/api/users')?.body).toMatchObject({ userName: 'mona', roleId: 'r-9', storeId: null });
});

test('a manager is added to the store they work at', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/users/');
  await page.getByRole('button', { name: /Add user/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Add user' });
  await dialog.getByLabel('User name').fill('omar');
  await dialog.getByLabel('Password').fill('Omar-pass-1');
  await dialog.getByLabel('Role').selectOption({ label: 'Manager' });
  await dialog.getByLabel('Home store').selectOption({ label: 'WEST · Westside Mall' });
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('omar is saved.')).toBeVisible();
  expect(api.last('POST', '/api/users')?.body).toMatchObject({ userName: 'omar', roleId: 'r-5', storeId: 's-west' });
});

test('the user form saves an e-mail to sign in with, and checks it', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/users/');
  // The list shows each user's e-mail.
  await expect(page.getByRole('cell', { name: 'kofi@example.com' })).toBeVisible();
  await page.getByRole('button', { name: /Add user/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Add user' });
  await dialog.getByLabel('User name').fill('nana');
  await dialog.getByLabel('Password').fill('Nana-pass-1');
  await dialog.getByLabel('Home store').selectOption({ label: 'MAIN · Downtown' });

  // Not an e-mail: nothing is sent.
  await dialog.getByLabel('E-mail').fill('nana at example.com');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Type the e-mail as name@example.com: one @, a dot after it, and no spaces.')).toBeVisible();
  expect(api.last('POST', '/api/users')).toBeUndefined();

  // Another user's e-mail: the server says so.
  await dialog.getByLabel('E-mail').fill('KOFI@example.com');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Another user has that e-mail.')).toBeVisible();

  await dialog.getByLabel('E-mail').fill(' Nana.Owusu@Example.com ');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('nana is saved.')).toBeVisible();
  expect(api.last('POST', '/api/users')?.body).toMatchObject({ userName: 'nana', email: 'Nana.Owusu@Example.com', storeId: 's-main' });
});

test('changing a user keeps or removes their e-mail', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/users/');
  await page.getByRole('cell', { name: 'kofi', exact: true }).click();
  await page.getByRole('button', { name: /Edit user/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Change kofi' });
  await expect(dialog.getByLabel('E-mail')).toHaveValue('kofi@example.com');
  await dialog.getByLabel('Full name').fill('Kofi B.');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog).toBeHidden();
  expect(api.last('PUT', '/api/users/u-2')?.body).toMatchObject({ fullName: 'Kofi B.', email: 'kofi@example.com', version: 'v-1' });

  // An empty e-mail is sent as empty, which removes it.
  await page.getByRole('button', { name: /Edit user/ }).click();
  await dialog.getByLabel('E-mail').fill('');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog).toBeHidden();
  expect(api.last('PUT', '/api/users/u-2')?.body).toMatchObject({ email: '' });
});
