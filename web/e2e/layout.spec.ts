/**
 * Runs at 1366×768 (desktop project) and 390×844 (phone project): no page scrolls sideways; wide tables scroll inside
 * their card; on a phone the navigation is a drawer.
 */
import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

const PAGES = ['/', '/tills/', '/sales/', '/sales/view/?id=sale-1', '/items/', '/items/edit/?id=i-1', '/clients/', '/suppliers/', '/stock/', '/gift-cards/', '/reports/', '/reports/view/?key=daily', '/staff/', '/users/', '/roles/', '/stores/', '/settings/', '/audit/', '/packages/', '/account/'];

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

test('the sign-in page fits too', async ({ page }) => {
  await mockApi(page, { user: null });
  await page.goto('/sign-in/');
  await expect(page.getByRole('button', { name: /Log in/ })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
});

test('the navigation is a panel on a wide screen and a drawer on a phone', async ({ page }, info) => {
  await mockApi(page);
  await page.goto('/');
  await expect(page.getByTestId('tile-Net sales')).toBeVisible();
  const panel = page.locator('[data-shell="nav"]');
  if (info.project.name === 'phone') {
    await expect(panel).toBeHidden();
    await page.getByRole('button', { name: 'Open the menu' }).click();
    const drawer = page.getByRole('dialog', { name: 'Menu' });
    await expect(drawer).toBeVisible();
    await drawer.getByRole('link', { name: /Gift cards/ }).click();
    await expect(page).toHaveURL(/\/gift-cards\/$/);
    await expect(drawer).toBeHidden();
  } else {
    await expect(panel).toBeVisible();
    await expect(panel.getByRole('link', { name: /Dashboard/ })).toHaveAttribute('aria-current', 'page');
    await expect(page.getByRole('button', { name: 'Open the menu' })).toBeHidden();
  }
});

test('a wide table scrolls inside its card', async ({ page }, info) => {
  test.skip(info.project.name !== 'phone', 'Only a phone is narrow enough.');
  await mockApi(page);
  await page.goto('/reports/view/?key=daily');
  const scroller = page.locator('.grid-scroll').first();
  await expect(scroller).toBeVisible();
  const { scrollWidth, clientWidth } = await scroller.evaluate((el) => ({ scrollWidth: el.scrollWidth, clientWidth: el.clientWidth }));
  expect(scrollWidth).toBeGreaterThan(clientWidth);
});
