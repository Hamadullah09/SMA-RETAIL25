import { expect, test } from '@playwright/test';
import { superAdmin } from './fixtures';
import { mockApi } from './mock-api';

test('a signed-out visitor goes to the sign-in page and back to where they were going', async ({ page }) => {
  const api = await mockApi(page, { user: null });
  await page.goto('/sales/');
  await expect(page).toHaveURL(/\/sign-in\/\?next=%2Fsales%2F$/);

  await page.getByLabel('User name').fill('sara');
  await page.getByLabel('Password', { exact: true }).fill('wrong');
  await page.getByRole('button', { name: /Log in/ }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Wrong user name or password' })).toBeVisible();
  await expect(page).toHaveURL(/\/sign-in\//);

  await page.getByLabel('Password', { exact: true }).fill('secret');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/sales\/$/);
  await expect(page.getByRole('heading', { name: 'Sales Journal' })).toBeVisible();

  const signIn = api.last('POST', '/api/auth/sign-in');
  expect(signIn?.body).toEqual({ userName: 'sara', password: 'secret' });
  expect(signIn?.headers['x-requested-with']).toBe('retailplus-web');

  // The token stays with the server's httpOnly cookie: nothing in JavaScript's reach holds it.
  const stored = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, cookie: document.cookie }));
  expect(stored).not.toContain('not-for-javascript');
});

test('a user signs in with their e-mail instead of the user name, in any case', async ({ page }) => {
  const api = await mockApi(page, { user: null });
  await page.goto('/sign-in/');
  await expect(page.getByText('Sign in with your user name or e-mail and your password.')).toBeVisible();

  await page.getByLabel('User name or e-mail').fill('  Kofi@Example.com ');
  await page.getByLabel('Password', { exact: true }).fill('secret');
  await page.getByRole('button', { name: /Log in/ }).click();
  await expect(page).not.toHaveURL(/\/sign-in\//);
  expect(api.last('POST', '/api/auth/sign-in')?.body).toEqual({ userName: 'Kofi@Example.com', password: 'secret' });
  // The server answers with the account the e-mail belongs to.
  await expect(page.getByTitle('Change password').first()).toContainText('Kofi Boateng');
});

test('the sign-in asks for a name or e-mail and a password', async ({ page }) => {
  const api = await mockApi(page, { user: null });
  await page.goto('/sign-in/');
  await page.getByRole('button', { name: /Log in/ }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Type your user name or e-mail and your password.' })).toBeVisible();
  expect(api.last('POST', '/api/auth/sign-in')).toBeUndefined();
});

test('signing out goes back to the sign-in page', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/tills/');
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in\/$/);
  expect(api.last('POST', '/api/auth/sign-out')?.headers['x-requested-with']).toBe('retailplus-web');
});

test('the first sign-in with a temporary password lands on Change password', async ({ page }) => {
  await mockApi(page, { user: { ...superAdmin, mustChangePassword: true } });
  await page.goto('/');
  await expect(page).toHaveURL(/\/account\/$/);
  await expect(page.getByText('Choose a new password before you go on.')).toBeVisible();

  // Other pages wait until it is done.
  await page.goto('/items/');
  await expect(page).toHaveURL(/\/account\/$/);

  await page.getByLabel('Current password').fill('Temp-1234');
  await page.getByLabel('New password', { exact: true }).fill('a long new password');
  await page.getByLabel('New password again').fill('a long new password');
  await page.getByRole('button', { name: /Change password/ }).click();
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByText('Net sales').first()).toBeVisible();
});
