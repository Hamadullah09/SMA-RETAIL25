import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('Ctrl+K opens Go to: type, choose with the arrows, Enter goes, Esc closes', async ({ page }) => {
  await mockApi(page);
  await page.goto('/');
  await expect(page.getByTestId('tile-Net sales')).toBeVisible();

  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Go to' });
  await expect(palette).toBeVisible();
  await expect(palette.getByRole('combobox')).toBeFocused();
  await expect(palette.getByRole('option').first()).toHaveText(/Go to Dashboard.*Ctrl\+1.*Screen/);

  await page.keyboard.press('Escape');
  await expect(palette).toBeHidden();

  await page.getByRole('button', { name: /Go to/ }).click();
  await page.keyboard.type('aud');
  await expect(palette.getByRole('option').first()).toHaveText(/Go to Audit log/);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/audit\/$/);
  await expect(page.getByRole('heading', { name: 'Audit log' })).toBeVisible();

  // Reports from the catalog and actions are there too.
  await page.keyboard.press('Control+k');
  await page.keyboard.type('daily');
  await expect(palette.getByRole('option').first()).toHaveText(/Daily sales summary.*Report/);
  await page.keyboard.press('Control+a');
  await page.keyboard.type('new item');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowUp');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/items\/edit\/$/);
});

test('Ctrl+1 … Ctrl+0 open the first ten pages and / finds in a list', async ({ page }) => {
  await mockApi(page);
  await page.goto('/');
  await expect(page.getByTestId('tile-Net sales')).toBeVisible();

  await page.keyboard.press('Control+4');
  await expect(page).toHaveURL(/\/items\/$/);
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
  await page.keyboard.press('/');
  await expect(page.getByPlaceholder('Stock code or description')).toBeFocused();

  await page.keyboard.press('Control+0');
  await expect(page).toHaveURL(/\/staff\/$/);
  await page.keyboard.press('Control+2');
  await expect(page).toHaveURL(/\/tills\/$/);

  // A button's key presses it: Ctrl+N on Tills enrols a till.
  await page.keyboard.press('Control+n');
  const enrol = page.getByRole('dialog', { name: 'Enrol a till' });
  await expect(enrol).toBeVisible();
  await enrol.getByRole('button', { name: /Get a code/ }).click();
  await expect(page.getByTestId('enrolment-code')).toHaveText('K7Q2-9XMB');
});
