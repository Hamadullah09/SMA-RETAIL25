/**
 * The HR pages at 1366×768 (desktop project) and 390×844 (phone project, as `*layout.spec.ts`): no page scrolls
 * sideways, and on a phone the HR pages are in the drawer under their heading.
 */
import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

const PAGES = [
  '/hr/employees/',
  '/hr/employees/edit/?id=e-1',
  '/hr/attendance/',
  '/hr/shifts/',
  '/hr/leave/',
  '/hr/payroll/',
  '/hr/payroll/run/?id=run-2',
  '/hr/payroll/payslip/?id=ps-1',
  '/hr/reports/',
  '/hr/settings/',
];

for (const path of PAGES) {
  test(`no sideways scroll on ${path}`, async ({ page }) => {
    await mockApi(page);
    await page.goto(path);
    await expect(page.locator('[data-shell="main"] h1').first()).toBeVisible();
    await page.waitForTimeout(150);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow, `the page is ${overflow}px wider than the screen`).toBeLessThanOrEqual(0);
  });
}

test('the phone drawer lists the HR pages under HR', async ({ page }, info) => {
  test.skip(info.project.name !== 'phone', 'Only a phone has the drawer.');
  await mockApi(page);
  await page.goto('/hr/employees/');
  await page.getByRole('button', { name: 'Open the menu' }).click();
  const drawer = page.getByRole('dialog', { name: 'Menu' });
  await expect(drawer.locator('[data-nav-group="HR"]')).toHaveText('HR');
  await drawer.getByRole('link', { name: /^Leave/ }).click();
  await expect(page).toHaveURL(/\/hr\/leave\/$/);
});
