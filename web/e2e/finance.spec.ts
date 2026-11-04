import { expect, test } from '@playwright/test';
import { accountant, financeViewer } from './finance-fixtures';
import { supervisor } from './fixtures';
import { mockApi } from './mock-api';

test('an accountant finds the books under Finance; a supervisor does not', async ({ page }) => {
  await mockApi(page, { user: accountant });
  await page.goto('/finance/');
  await expect(page.getByRole('heading', { name: 'Finance', level: 1 })).toBeVisible();
  await expect(page.getByTestId("tile-This month's profit")).toHaveText('$11,000.00');
  await expect(page.getByTestId('tile-In suspense')).toHaveText('$120.00');
  await expect(page.getByRole('table').getByRole('row', { name: /HBL current/ })).toContainText('$25,000.00');

  const nav = page.getByRole('navigation', { name: 'Pages' });
  for (const name of ['Chart of accounts', 'Journals', 'Bills', 'Supplier payments', 'Expenses', 'Bank and cash', 'Statements', 'Budgets', 'Years and periods', 'Suspense', 'Finance setup']) {
    // The exact title: "Bills" is not "Bills of materials" (manufacturing), which this user also sees.
    await expect(nav.getByRole('link', { name, exact: true })).toBeVisible();
  }

  await mockApi(page, { user: supervisor });
  await page.goto('/finance/');
  await expect(page.getByText('Your role cannot open this page.')).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Pages' }).getByRole('link', { name: /^Statements/ })).toHaveCount(0);
});

test('someone who may only look at the books gets no buttons that change them', async ({ page }) => {
  await mockApi(page, { user: financeViewer });
  await page.goto('/finance/');
  await expect(page.getByTestId('tile-Receivables')).toHaveText('$3,200.00');
  await expect(page.getByRole('link', { name: /New bill/ })).toHaveCount(0);
  await page.goto('/finance/years/');
  await expect(page.getByRole('table', { name: 'Months of 2026-27' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Lock|Close the year/ })).toHaveCount(0);
  await page.goto('/finance/reports/');
  await expect(page.getByRole('link', { name: /Profit and loss/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: /accountant’s program/ })).toHaveCount(0);
});

test('a journal entry by hand must balance, goes for approval, and another person approves it', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/journals/edit/');
  await expect(page.getByRole('heading', { name: 'New journal entry' })).toBeVisible();
  await page.getByLabel('Description').fill('Accrue the electricity bill');
  await page.getByLabel('Account of line 1').selectOption({ label: '6110 Electricity' });
  await page.getByLabel('Debit of line 1').fill('300');
  await page.getByLabel('Account of line 2').selectOption({ label: '2010 Accounts payable' });
  await page.getByLabel('Credit of line 2').fill('250');
  await expect(page.getByText('Off by $50.00')).toBeVisible();
  await page.getByRole('button', { name: /Save for approval/ }).click();
  await expect(page.getByText('The entry does not balance: it is $50.00 off.')).toBeVisible();
  expect(api.last('POST', '/api/finance/journals')).toBeUndefined();

  await page.getByLabel('Credit of line 2').fill('300');
  await expect(page.getByText('Balanced')).toBeVisible();
  await page.getByRole('button', { name: /Save for approval/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/journals')?.body).toMatchObject({
    memo: 'Accrue the electricity bill',
    storeId: null,
    post: false,
    lines: [
      { accountId: 'e-power', debit: 300, credit: 0 },
      { accountId: 'l-ap', debit: 0, credit: 300 },
    ],
  });

  // Sara made the draft; Nadia approves it.
  await page.goto('/finance/journals/edit/?id=je-20');
  await expect(page.getByText(/Someone other than sara approves this entry/)).toBeVisible();
  await page.getByRole('button', { name: /Approve and post/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/journals/je-20/approve')?.headers['x-requested-with']).toBe('retailplus-web');
  await expect(page.getByRole('heading', { name: 'Entry 21' })).toBeVisible();
});

test('a statement is run per store or for the company, compared with the budget, and the ledger goes to Tally', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/reports/');
  await expect(page.getByText('For the whole company', { exact: true })).toBeVisible();

  // Exports cover the days chosen; a store's file is the store's.
  const download = page.waitForEvent('download');
  await page.getByRole('listitem').filter({ hasText: 'Tally' }).getByRole('button', { name: /Download/ }).click();
  expect((await download).suggestedFilename()).toMatch(/^ledger-\d{8}-\d{8}\.xml$/);
  const sent = api.last('GET', '/api/finance/export');
  expect(sent?.query.get('format')).toBe('tally');
  expect(sent?.query.get('storeId')).toBeNull();
  expect(sent?.query.get('from')).toMatch(/^\d{4}-\d{2}-01$/);

  await page.getByRole('link', { name: /Profit and loss/ }).click();
  await expect(page).toHaveURL(/key=profit-and-loss/);
  await expect(page.getByRole('heading', { name: 'Profit and loss', level: 1 })).toBeVisible();
  const table = page.getByRole('table', { name: 'Profit and loss' });
  await expect(table.getByRole('row', { name: /Net profit/ })).toHaveAttribute('data-row', 'total');
  await expect(table.getByRole('row', { name: /Total income/ })).toHaveAttribute('data-row', 'total');
  await expect(table.getByRole('row', { name: /Rent/ })).not.toHaveAttribute('data-row', /.+/);
  await expect(table.getByRole('row', { name: /^6000 Expenses/ })).toHaveAttribute('data-row', 'heading');

  await page.getByLabel('Compared with').selectOption('budget');
  await page.getByRole('button', { name: /^Run/ }).click();
  await expect.poll(() => api.last('GET', '/api/finance/reports/profit-and-loss')?.query.get('compare')).toBe('budget');
  await expect(page).toHaveURL(/compare=budget/);

  // The store chosen at the top runs the statement for that store.
  await page.locator('#store-picker').selectOption('s-west');
  await expect.poll(() => api.last('GET', '/api/finance/reports/profit-and-loss')?.query.get('storeId')).toBe('s-west');

  // The account ledger waits for its account.
  await page.goto('/finance/reports/view/?key=account-ledger');
  await expect(page.getByText('Choose the account above, then Run.')).toBeVisible();
  await page.getByLabel('Account', { exact: true }).selectOption({ label: '6100 Rent' });
  await page.getByRole('button', { name: /^Run/ }).click();
  await expect.poll(() => api.last('GET', '/api/finance/reports/account-ledger')?.query.get('accountId')).toBe('e-rent');
});

test('a budget is typed or spread over the year and only what changed is saved', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/budgets/');
  await expect(page.getByLabel('Year', { exact: true })).toHaveValue('y-2627');
  // Income is typed as a positive amount.
  await expect(page.getByLabel('Sales, Jul 2026')).toHaveValue('10000');
  await expect(page.getByText('Everything is saved.')).toBeVisible();

  await page.getByRole('button', { name: 'Spread a year’s amount for Electricity' }).click();
  const dialog = page.getByRole('dialog', { name: 'Spread Electricity' });
  await dialog.getByLabel('For the year').fill('1200');
  await dialog.getByRole('button', { name: /^Spread/ }).click();
  await expect(page.getByLabel('Electricity, Aug 2026')).toHaveValue('100');
  await expect(page.getByTestId('year-6110')).toHaveText('$1,200.00');
  await page.getByLabel('Sales, Jul 2026').fill('12000');
  await expect(page.getByText('13 changes not saved')).toBeVisible();

  await page.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/finance/budgets')?.query.get('yearId')).toBe('y-2627');
  const amounts = (api.last('PUT', '/api/finance/budgets')?.body as { amounts: Array<{ accountId: string; month: string; amount: number }> }).amounts;
  expect(amounts).toHaveLength(13);
  expect(amounts).toContainEqual({ accountId: 'i-sales', month: '2026-07-01', amount: -12000 });
  expect(amounts.filter((a) => a.accountId === 'e-power').every((a) => a.amount === 100)).toBe(true);
});

test('months are locked and opened, and a year is closed after a warning', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/years/');
  const months = page.getByRole('table', { name: 'Months of 2026-27' });
  await months.getByRole('button', { name: 'Open Jul 2026' }).click();
  await expect.poll(() => api.last('POST', '/api/finance/periods/p-1/unlock')?.headers['x-requested-with']).toBe('retailplus-web');
  await expect(months.getByRole('button', { name: 'Lock Jul 2026' })).toBeVisible();
  await months.getByRole('button', { name: 'Lock Aug 2026' }).click();
  await expect.poll(() => api.last('POST', '/api/finance/periods/p-2/lock')).toBeTruthy();

  await page.getByRole('button', { name: /Close the year/ }).click();
  const confirm = page.getByRole('dialog', { name: 'Close 2026-27?' });
  await expect(confirm).toContainText('retained earnings');
  await confirm.getByRole('button', { name: /Close the year/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/years/y-2627/close')).toBeTruthy();
  await expect(page.getByRole('button', { name: /Open the year again/ })).toBeVisible();
  await expect(months.getByRole('button')).toHaveCount(0);
});

test('a bank statement is uploaded with its layout, matched, and what only the bank knew is entered', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/bank/reconcile/?id=b-hbl');
  await expect(page.getByRole('heading', { name: 'Reconcile HBL current' })).toBeVisible();
  await expect(page.getByTestId('figure-Difference')).toHaveText('$0.00');
  await expect(page.getByTestId('figure-Not yet in the books')).toHaveText('-$50.00');

  // The layout kept for HBL comes back: two columns for money in and out.
  await page.getByRole('button', { name: /Upload a statement/ }).click();
  const upload = page.getByRole('dialog', { name: 'Upload a statement' });
  await expect(upload.getByLabel('Money in and out are in two columns')).toBeChecked();
  await expect(upload.getByLabel('Money out column')).toHaveValue('Withdrawal');
  await upload.getByLabel('Statement file').setInputFiles({ name: 'hbl-october.csv', mimeType: 'text/csv', buffer: Buffer.from('Date,Narration,Withdrawal,Deposit,Balance\r\n') });
  await upload.getByRole('button', { name: /^Upload/ }).click();
  await expect(upload).toBeHidden();
  const form = String(api.last('POST', '/api/finance/bank-accounts/b-hbl/statements')?.body);
  expect(form).toContain('hbl-october.csv');
  expect(form).toContain('name="debitColumn"');
  expect(form).not.toContain('name="amountColumn"');

  // The deposit matches the one of the day before, not the older one of the same amount.
  await page.getByRole('button', { name: /Match the line of .* for \$5,050\.00/ }).click();
  const match = page.getByRole('dialog', { name: /Match \$5,050\.00/ });
  await expect(match.getByRole('listitem')).toHaveCount(1);
  await expect(match).toContainText('entry 11');
  await match.getByRole('button', { name: /This one/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/statement-lines/sl-2/match')?.body).toEqual({ journalLineId: 'jl-2' });

  // The bank's charges become an entry of their own.
  await page.getByRole('button', { name: /Enter the line of .* for -\$100\.00/ }).click();
  const enter = page.getByRole('dialog', { name: /Enter -\$100\.00/ });
  await expect(enter.getByLabel('Account')).toHaveValue('');
  await enter.getByRole('button', { name: /Post it/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/statement-lines/sl-3/entry')?.body).toEqual({ accountId: null, memo: 'Service charges' });
});

test('suspense says what found no account, and the days are posted again', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/suspense/');
  await expect(page.getByRole('row', { name: /Takings JazzCash/ })).toContainText('None mapped');
  await page.getByRole('button', { name: /Post days/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Post days to the ledger' });
  await dialog.getByRole('button', { name: /^Post/ }).click();
  await expect(dialog).toContainText('3 business days were posted');
  const body = api.last('POST', '/api/finance/post-days')?.body as { from: string; to: string; storeId: string | null };
  expect(body.storeId).toBeNull();
  expect(body.from <= body.to).toBe(true);
});

test('the daily journal’s account mapping is changed for a store', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/settings/?tab=mapping');
  await expect(page.getByRole('tab', { name: 'Account mapping' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('1 without an account')).toBeVisible();
  await page.getByLabel('Account for Money taken as JazzCash').selectOption({ label: '1020 HBL current account' });
  await page.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/finance/mapping')?.query.get('storeId')).toBe('s-main');
  expect(api.last('PUT', '/api/finance/mapping')?.body).toContainEqual({ kind: 'Tender', key: 'JazzCash', accountCode: '1020', accountName: null });

  await page.getByRole('tab', { name: 'The books' }).click();
  await expect(page.getByLabel('The year starts in')).toHaveValue('7');
  await page.getByRole('tab', { name: 'Tax rules' }).click();
  await expect(page.getByRole('region', { name: 'Pakistan 2026-27' })).toContainText('5% filers, 10% others');
});

test('bills are paid together, debit notes set against them, tax kept back', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/payments/');
  await page.getByRole('button', { name: /Pay bills/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Pay bills' });
  await dialog.getByLabel('Pay bill 101').check();
  await dialog.getByLabel('Pay debit note 103').check();
  await expect(dialog.getByRole('list', { name: 'Payments to make' })).toContainText('$101,700.00 paid, $4,500.00 withheld at 5% (153(1)(a))');
  await dialog.getByRole('button', { name: 'Pay $101,700.00' }).click();
  await expect.poll(() => api.last('POST', '/api/finance/payment-runs')?.body).toMatchObject({
    bankAccountId: 'b-hbl',
    withholdingAmount: null,
    bills: [
      { billId: 'bill-1', amount: 118000 },
      { billId: 'dn-1', amount: 11800 },
    ],
  });
  await expect(dialog).toBeHidden();
});

test('an expense needs who was paid, then is saved and posted', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/expenses/?new=1');
  const dialog = page.getByRole('dialog', { name: 'New expense' });
  await dialog.getByRole('button', { name: /Save and post/ }).click();
  await expect(dialog.getByText('Say who was paid.')).toBeVisible();
  await dialog.getByLabel('Paid to').fill('K-Electric');
  await dialog.getByLabel('Account', { exact: true }).selectOption({ label: '6110 Electricity' });
  await dialog.getByLabel('Amount before tax').fill('300');
  await dialog.getByLabel('Paid', { exact: true }).selectOption('Bank');
  await dialog.getByLabel('Bank account').selectOption({ label: 'HBL current (PKR)' });
  await dialog.getByRole('button', { name: /Save and post/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/expenses')?.body).toMatchObject({
    storeId: 's-main',
    payee: 'K-Electric',
    accountId: 'e-power',
    amount: 300,
    taxAmount: 0,
    paidBy: 'Bank',
    bankAccountId: 'b-hbl',
    post: true,
  });
  await expect(page.getByText('Expense 32 is posted.')).toBeVisible();
});

test('a bill adds up its lines with tax and is saved and posted', async ({ page }) => {
  const api = await mockApi(page, { user: accountant });
  await page.goto('/finance/bills/edit/');
  await page.getByLabel('Supplier', { exact: true }).selectOption({ label: 'Cotton Co. Ltd' });
  await page.getByLabel('Account of line 1').selectOption({ label: '6100 Rent' });
  await page.getByLabel('Price of line 1').fill('1000');
  await page.getByLabel('Tax rate of line 1').fill('18');
  await expect(page.getByTestId('bill-total')).toHaveText('$1,180.00');
  await page.getByRole('button', { name: /Save and post/ }).click();
  await expect.poll(() => api.last('POST', '/api/finance/bills')?.body).toMatchObject({
    kind: 'Bill',
    supplierId: 'sp-1',
    storeId: 's-main',
    lines: [{ accountId: 'e-rent', quantity: 1, unitPrice: 1000, taxRate: 18 }],
  });
  await expect.poll(() => api.last('POST', '/api/finance/bills/bill-new/post')).toBeTruthy();
});
