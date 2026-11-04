/**
 * Screenshots of the main pages at the shops' screen size (1366×768), a tablet (1024×768) and a phone (390×844), to set
 * beside the desktop app's own screenshots. `npm run screens` writes them to SCREENS_DIR (default: test-results/screens).
 */
import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { mockApi } from './mock-api';

const dir = process.env.SCREENS_DIR ?? join(__dirname, '..', 'test-results', 'screens');
mkdirSync(dir, { recursive: true });

const PAGES: Array<[name: string, path: string, ready: string]> = [
  ['01-dashboard', '/', 'Net sales'],
  ['02-tills', '/tills/', 'Front counter'],
  ['03-sales', '/sales/', '10000041'],
  ['04-sale', '/sales/view/?id=sale-1', 'Cotton T-shirt, medium'],
  ['05-items', '/items/', 'Slim jeans 32'],
  ['06-item', '/items/edit/?id=i-1', 'Pricing'],
  ['07-clients', '/clients/', 'Akua Owusu'],
  ['08-stock', '/stock/', 'Cotton T-shirt, medium'],
  ['09-gift-cards', '/gift-cards/', '6035 0000 1234'],
  ['10-reports', '/reports/', 'Daily sales summary'],
  ['11-report', '/reports/view/?key=daily', 'Customers'],
  ['12-staff', '/staff/', 'Still in'],
  ['13-users', '/users/', 'Must set password'],
  ['14-roles', '/roles/', 'Weekend help'],
  ['15-settings', '/settings/', 'Business name'],
  ['16-audit', '/audit/', 'Void sale'],
  ['17-packages', '/packages/', 'Sent before'],
  ['18-stores', '/stores/', 'Waiting to join'],
  ['19-suppliers', '/suppliers/', 'Cotton Co. Ltd'],
  ['20-crm', '/crm/', 'Uniforms for Kim Design'],
  ['21-tasks', '/tasks/', 'Call Daniel about the uniforms'],
  ['22-segments', '/segments/', 'Big spenders'],
  ['23-campaigns', '/campaigns/', 'Spring sale'],
  ['24-campaign', '/campaigns/view/?id=cm-1', 'Grace Adjei'],
  ['25-promotions', '/promotions/', 'Socks 3 for 2'],
  ['26-targets', '/targets/', 'The whole store'],
];

const SIZES = [
  ['desktop', { width: 1366, height: 768 }],
  ['tablet', { width: 1024, height: 768 }],
  ['phone', { width: 390, height: 844 }],
] as const;

for (const [size, viewport] of SIZES) {
  test.describe(size, () => {
    test.use({ viewport });

    test('sign-in', async ({ page }) => {
      await mockApi(page, { user: null });
      await page.goto('/sign-in/');
      await expect(page.getByRole('button', { name: /Log in/ })).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-00-sign-in.png`) });
    });

    for (const [name, path, ready] of PAGES) {
      test(name, async ({ page }) => {
        await mockApi(page);
        await page.goto(path);
        await expect(page.getByText(ready, { exact: false }).filter({ visible: true }).first()).toBeVisible();
        await page.waitForTimeout(300);
        await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-${name}.png`) });
      });
    }

    test('dialogs', async ({ page }) => {
      await mockApi(page);
      await page.goto('/clients/');
      await page.getByRole('row', { name: /Daniel Kim/ }).dblclick();
      await expect(page.getByRole('dialog').getByLabel('City')).toHaveValue('Toronto');
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-21-client-dialog.png`) });

      await page.goto('/audit/');
      await page.getByRole('row', { name: /Changed/ }).click();
      await expect(page.getByRole('table', { name: 'Before and after' })).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-22-audit-drawer.png`) });

      await page.goto('/tills/?enrol=1');
      await page.getByRole('button', { name: /Get a code/ }).click();
      await expect(page.getByTestId('enrolment-code')).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-23-enrolment-code.png`) });
    });

    test('departments and categories', async ({ page }) => {
      await mockApi(page);
      await page.goto('/items/');
      await page.getByRole('button', { name: /Departments and categories/ }).click();
      await expect(page.getByRole('dialog', { name: 'Departments and categories' }).getByText('Footwear')).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-26-departments.png`) });
    });

    test('transfers', async ({ page }) => {
      await mockApi(page);
      await page.goto('/stock/');
      await page.getByRole('tab', { name: 'Transfers' }).click();
      await expect(page.getByPlaceholder('Transfer number or location')).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-27-transfers.png`) });
    });

    test('every store', async ({ page }) => {
      await mockApi(page);
      await page.goto('/items/edit/?id=i-1');
      await page.getByLabel(/Apply to every store/).check();
      await page.getByRole('tab', { name: 'Pricing' }).click();
      await page.getByLabel('Price 1').fill('19.95');
      await page.getByRole('button', { name: /^Save/ }).first().click();
      await expect(page.getByRole('dialog', { name: /Save this change at 2 locations/ })).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-28-every-store.png`) });
    });

    test('hub key', async ({ page }) => {
      await mockApi(page);
      await page.goto('/stores/');
      await page.getByRole('region', { name: 'RFID hubs of Downtown' }).getByRole('button', { name: /Add a hub key/ }).click();
      await page.getByLabel('Hub name').fill('Back room PC');
      await page.getByRole('button', { name: /Make the key/ }).click();
      await expect(page.getByTestId('hub-key')).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-29-hub-key.png`) });
    });

    test('report printout', async ({ page }) => {
      await mockApi(page);
      await page.goto('/reports/view/?key=daily');
      await expect(page.getByRole('table', { name: 'Daily sales summary' })).toBeVisible();
      await page.emulateMedia({ media: 'print' });
      await page.screenshot({ animations: 'disabled', fullPage: true, path: join(dir, `${size}-24-report-print.png`) });
    });

    test('phone menu', async ({ page }) => {
      test.skip(size !== 'phone', 'The drawer is the phone navigation.');
      await mockApi(page);
      await page.goto('/sales/');
      await page.getByRole('button', { name: 'Open the menu' }).click();
      await expect(page.getByRole('dialog', { name: 'Menu' })).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-25-menu.png`) });
    });

    test('palette', async ({ page }) => {
      await mockApi(page);
      await page.goto('/');
      await expect(page.getByText('Net sales').first()).toBeVisible();
      await page.keyboard.press('Control+k');
      await expect(page.getByRole('dialog', { name: 'Go to' })).toBeVisible();
      await page.screenshot({ animations: 'disabled', path: join(dir, `${size}-20-palette.png`) });
    });
  });
}
