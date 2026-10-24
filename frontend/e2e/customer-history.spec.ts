import { expect, test, type Page } from '@playwright/test';
import { CREDENTIALS, openTill, payExactCash, ringOneItem, signIn } from './support/till';

/**
 * A customer's purchases, from both places a shop asked for them.
 *
 * Clicking a customer shows what they have bought, filterable, and clicking a receipt opens that
 * invoice — on the customer screen, and from the till's client window so a cashier can answer
 * "what did I buy last time?" without leaving the sale. The server's half (a cashier may read it,
 * and only that customer's sales come back) is pinned by CustomerSalesTests; this is the half only a
 * browser can show: that both screens get there, and that the sale just rung is the one listed.
 */

async function createCustomer(page: Page, lastName: string): Promise<void> {
  await page.goto('/customers');
  await page.getByRole('button', { name: 'New customer' }).click();
  await page.getByLabel('First name').fill('E2E');
  await page.getByLabel('Last name').fill(lastName);
  await page.getByRole('button', { name: 'Save', exact: true }).first().click();

  // A create now reopens the panel as the new customer, with their history first.
  await expect(page.getByRole('tab', { name: 'Sales history' })).toBeVisible({ timeout: 15_000 });
}

test.describe("a customer's purchases", () => {
  test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the sign-in end-to-end tests.');

  // One journey across two screens — create, ring, pay, look up twice — which is more than the
  // default thirty seconds on a dev server compiling each page the first time it is opened.
  test.setTimeout(120_000);

  test('a sale rung for a customer appears in their history at the till and on the customer screen', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1600, height: 900 });
    await signIn(page);

    const lastName = `History${Date.now().toString(36)}`;
    await createCustomer(page, lastName);

    await openTill(page);

    // Attach them with F5, then ring and pay.
    await page.keyboard.press('F5');
    const client = page.getByRole('dialog', { name: 'Client' });
    await client.getByPlaceholder('Name, company or account number').fill(lastName);
    await client.getByRole('button', { name: new RegExp(`E2E ${lastName}`) }).first().click();
    await expect(page.getByRole('dialog')).toHaveCount(0);

    await ringOneItem(page);
    const receipt = await payExactCash(page);
    await page.getByRole('dialog', { name: 'Sale complete' }).getByRole('button', { name: /Next customer/ }).click();

    // At the till: F5, find them, open their purchases, open the receipt.
    await page.keyboard.press('F5');
    await page.getByRole('dialog', { name: 'Client' }).getByPlaceholder('Name, company or account number').fill(lastName);
    await page.getByRole('button', { name: new RegExp(`Purchases of E2E ${lastName}`) }).click();

    const purchases = page.getByRole('dialog', { name: new RegExp(`Purchases · E2E ${lastName}`) });
    await expect(purchases.getByText('1 sale')).toBeVisible();

    await purchases.getByRole('button', { name: new RegExp(`Receipt #${receipt}\\b`) }).click();

    const invoice = purchases.getByRole('article', { name: `Receipt ${receipt}` });
    await expect(invoice).toBeVisible();
    await expect(invoice.getByText(/toilet tissue/i).first()).toBeVisible();

    await purchases.getByRole('button', { name: 'All sales' }).click();
    await expect(purchases.getByRole('button', { name: new RegExp(`Receipt #${receipt}\\b`) })).toBeVisible();

    // A filter that matches nothing says so, rather than showing an empty box.
    await purchases.getByPlaceholder('Name, code or tag').fill('nothing-was-sold-like-this');
    await expect(purchases.getByText('No sales match these filters.')).toBeVisible();

    await page.keyboard.press('Escape');
    await page.keyboard.press('Escape');

    // On the customer screen: one click on the row opens their history.
    await page.goto('/customers');
    await page.getByLabel('Search customers').fill(lastName);
    await page.getByText(`E2E ${lastName}`).first().click();

    await expect(page.getByRole('tab', { name: 'Sales history' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('button', { name: new RegExp(`Receipt #${receipt}\\b`) })).toBeVisible();
  });
});
