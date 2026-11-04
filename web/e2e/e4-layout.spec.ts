/**
 * The E4 pages at 1366×768 (desktop project) and 390×844 (phone project, which runs every *layout.spec.ts): no page
 * scrolls sideways; the pipeline board and wide tables scroll inside their card.
 */
import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

const PAGES = ['/crm/', '/crm/?tab=leads', '/crm/?tab=activities', '/crm/?tab=report', '/tasks/', '/segments/', '/campaigns/', '/campaigns/?tab=templates', '/campaigns/view/?id=cm-1', '/promotions/', '/targets/'];

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

test('the new pages are in the navigation and in Ctrl+K', async ({ page }, info) => {
  test.skip(info.project.name === 'phone', 'The navigation panel is a drawer on a phone.');
  await mockApi(page);
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  for (const name of ['CRM', 'My tasks', 'Segments', 'Campaigns', 'Promotions', 'Targets']) {
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toBeVisible();
  }
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Go to' });
  await page.keyboard.type('new promotion');
  await expect(palette.getByRole('option').first()).toHaveText(/New promotion/);
  await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog', { name: 'New promotion' })).toBeVisible();
});
