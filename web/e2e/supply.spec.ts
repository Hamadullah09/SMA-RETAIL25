/**
 * The supply chain pages (phase E2) against the mocked server: the navigation group, receiving in batches with the
 * put-away list, requisitions and their approval, batches and expiry, bins and their lists, the three-way match, landed
 * costs, reorder suggestions and supplier performance. Approving is hidden from a role without purchasing.approve.
 */
import { expect, test } from '@playwright/test';
import { buyer, supervisor } from './fixtures';
import { mockApi } from './mock-api';

test('the supply chain pages sit under their heading, each for its permission', async ({ page }) => {
  await mockApi(page);
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  await expect(nav.getByText('Supply chain')).toBeVisible();
  for (const name of ['Purchase orders', 'Requisitions', 'Batches and expiry', 'Bins', 'Matching', 'Landed costs', 'Reorder suggestions', 'Supplier performance'])
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toBeVisible();

  // Ctrl+K finds them by what they hold, with the group beside them.
  await page.keyboard.press('Control+k');
  await page.getByRole('dialog', { name: 'Go to' }).getByRole('combobox').fill('freight');
  await expect(page.getByRole('dialog', { name: 'Go to' }).getByRole('option').first()).toHaveText(/Go to Landed costs.*Supply chain/);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/landed-costs\/$/);
});

test('a supervisor sees only the supplier report of the group', async ({ page }) => {
  await mockApi(page, { user: supervisor });
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  await expect(nav.getByRole('link', { name: /^Supplier performance/ })).toBeVisible();
  for (const name of ['Purchase orders', 'Requisitions', 'Batches and expiry', 'Bins', 'Matching', 'Landed costs', 'Reorder suggestions'])
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toHaveCount(0);
  await page.goto('/requisitions/');
  await expect(page.getByText('Your role cannot open this page.')).toBeVisible();
});

test('receiving an order asks for the batches of items kept in batches, then shows the put-away list', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/purchase-orders/');
  await page.getByRole('row', { name: /10000003/ }).click();
  const panel = page.getByRole('dialog', { name: 'Purchase order 10000003' });
  await expect(panel.getByRole('cell', { name: 'MK-500' })).toBeVisible();
  await panel.getByRole('button', { name: /^Receive/ }).click();

  const receive = page.getByRole('dialog', { name: 'Receive order 10000003' });
  // The expiry is offered from the item's 60-day shelf life.
  await expect(receive.getByLabel('Expiry of batch 1 of MK-500')).not.toHaveValue('');
  await receive.getByRole('button', { name: /Post the receipt/ }).click();
  await expect(receive.getByText('Every batch needs its number.')).toBeVisible();

  await receive.getByLabel('Batch number 1 of MK-500').fill('oat-0912');
  await receive.getByRole('button', { name: /Another batch/ }).click();
  await receive.getByLabel('Batch number 2 of MK-500').fill('OAT-0913');
  await receive.getByLabel('Pieces of batch 2 of MK-500').fill('4');
  await receive.getByRole('button', { name: /Post the receipt/ }).click();
  await expect(receive.getByText('The batches add up to 28, not the 24 pieces received.')).toBeVisible();
  await receive.getByLabel('Pieces of batch 1 of MK-500').fill('20');
  await receive.getByRole('button', { name: /Post the receipt/ }).click();

  await expect(page.getByText('Order 10000003 is received: stock and costs are updated.')).toBeVisible();
  const sent = api.last('POST', '/api/purchase-orders/po-3/receive')?.body as { version: string; lines: Array<{ lineId: string; batches: Array<{ batchNumber: string; pieces: number }> | null }> };
  expect(sent.version).toBe('v-1');
  expect(sent.lines.find((l) => l.lineId === 'po-3-l2')?.batches).toEqual([
    expect.objectContaining({ batchNumber: 'OAT-0912', pieces: 20 }),
    expect.objectContaining({ batchNumber: 'OAT-0913', pieces: 4 }),
  ]);
  expect(sent.lines.find((l) => l.lineId === 'po-3-l1')?.batches).toBeNull();

  const putAway = page.getByRole('dialog', { name: /Put-away list: PO 10000003/ });
  await expect(putAway.getByRole('cell', { name: 'C-02' })).toBeVisible();
  await expect(putAway.getByRole('cell', { name: 'OAT-0912' })).toBeVisible();
});

test('a draft order is posted from its panel', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/purchase-orders/');
  await page.getByRole('row', { name: /10000002/ }).click();
  await page.getByRole('dialog', { name: 'Purchase order 10000002' }).getByRole('button', { name: /Post order/ }).click();
  await expect(page.getByText('Order 10000002 is posted: the pieces are on order.')).toBeVisible();
  expect(api.last('POST', '/api/purchase-orders/po-2/post')?.body).toEqual({ version: 'v-1' });
});

test('a requisition is approved and made into purchase orders by someone who may approve', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/requisitions/');
  await expect(page.getByRole('region', { name: 'Approval limits' }).getByRole('cell', { name: 'Manager' })).toBeVisible();
  await page.getByRole('row', { name: /10000002/ }).click();
  const panel = page.getByRole('dialog', { name: 'Requisition 10000002' });
  await panel.getByRole('button', { name: /^Approve/ }).click();
  await expect(page.getByText('Requisition 10000002 is approved.')).toBeVisible();
  expect(api.last('POST', '/api/requisitions/rq-2/approve')?.body).toMatchObject({ version: 'v-Submitted' });

  await panel.getByRole('button', { name: /Make purchase orders/ }).click();
  await expect(panel.getByText('Purchase orders made:')).toBeVisible();
  await expect(panel.getByText('Order 10000010 · Sole Brothers')).toBeVisible();
});

test('a rejected requisition says why', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/requisitions/');
  await page.getByRole('row', { name: /10000002/ }).click();
  await page.getByRole('dialog', { name: 'Requisition 10000002' }).getByRole('button', { name: /^Reject/ }).click();
  const reject = page.getByRole('dialog', { name: 'Reject the requisition' });
  await reject.getByRole('button', { name: /^Reject/ }).click();
  await expect(reject.getByText('Say why it is rejected.')).toBeVisible();
  await reject.getByLabel('Why').fill('Too many');
  await reject.getByRole('button', { name: /^Reject/ }).click();
  await expect(page.getByText('Requisition 10000002 is rejected.')).toBeVisible();
  expect(api.last('POST', '/api/requisitions/rq-2/reject')?.body).toMatchObject({ note: 'Too many' });
});

test('a stock clerk asks for goods and sends them for approval, but cannot approve', async ({ page }) => {
  const api = await mockApi(page, { user: buyer });
  await page.goto('/requisitions/');
  await expect(page.getByRole('region', { name: 'Approval limits' })).toHaveCount(0);
  await page.getByRole('row', { name: /10000002/ }).click();
  const submitted = page.getByRole('dialog', { name: 'Requisition 10000002' });
  await expect(submitted.getByText('Waiting for someone who may approve purchases.')).toBeVisible();
  await expect(submitted.getByRole('button', { name: /^Approve/ })).toHaveCount(0);
  await expect(submitted.getByRole('button', { name: /^Reject/ })).toHaveCount(0);
  await submitted.getByRole('button', { name: /^Close/ }).click();

  await page.getByRole('button', { name: /New requisition/ }).click();
  const editor = page.getByRole('dialog', { name: 'New requisition' });
  await editor.getByLabel('Stock code of line 1').fill('ts-100-m');
  await editor.getByLabel('Pieces of line 1').fill('24');
  await editor.getByLabel('Note').fill('For the sale');
  await editor.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByText('Requisition 10000003 is saved.')).toBeVisible();
  expect(api.last('POST', '/api/requisitions')?.body).toMatchObject({ note: 'For the sale', lines: [expect.objectContaining({ plu: 'ts-100-m', quantity: 24 })] });
  expect(api.last('POST', '/api/requisitions')?.query.get('storeId')).toBe('s-main');

  const draft = page.getByRole('dialog', { name: 'Requisition 10000003' });
  await draft.getByRole('button', { name: /Send for approval/ }).click();
  await expect(page.getByText('Requisition 10000003 is sent for approval.')).toBeVisible();
});

test('batches show their stock and expiry; expired ones are adjusted out; the store can stop expired sales', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/batches/');
  const expired = page.getByRole('row', { name: /OAT-0811/ });
  await expect(expired.getByText('Expired 2 days ago')).toBeVisible();
  await expect(page.getByRole('row', { name: /YG-77/ }).getByText('12 days left')).toBeVisible();

  await page.getByRole('tab', { name: 'Expired' }).click();
  await expect(page.getByText('1 expired batches still hold 4 pieces worth $17.60.')).toBeVisible();
  await page.getByRole('row', { name: /OAT-0811/ }).getByRole('button', { name: 'Adjust' }).click();
  const adjust = page.getByRole('dialog', { name: 'Adjust batch OAT-0811' });
  await expect(adjust.getByLabel('Reason')).toHaveValue('Expired, thrown away');
  await adjust.getByLabel('Set to this count').check();
  await adjust.getByLabel('Set the stock to').fill('0');
  await adjust.getByRole('button', { name: /^Adjust/ }).click();
  await expect(page.getByText('Batch OAT-0811 now holds 0.')).toBeVisible();
  expect(api.last('POST', '/api/batches/bt-1/adjust')?.body).toEqual({ quantity: 0, setTo: true, reason: 'Expired, thrown away' });

  await page.getByRole('tab', { name: 'Expiring soon' }).click();
  await page.getByLabel('Days ahead').fill('14');
  await expect.poll(() => api.last('GET', '/api/batches/expiring')?.query.get('days')).toBe('14');

  await page.getByRole('button', { name: /Expiry settings/ }).click();
  const settings = page.getByRole('dialog', { name: 'Expiry settings' });
  await settings.getByLabel('Stop expired stock from being sold at the POS').uncheck();
  await settings.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByText('The settings are saved.')).toBeVisible();
  expect(api.last('PUT', '/api/supply/settings')?.body).toMatchObject({ blockExpiredSales: false, expiryWarningDays: 30, version: 'v-1' });
});

test('a batch is added to an item and its dates changed', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/batches/');
  await page.getByRole('button', { name: /Add a batch/ }).click();
  const add = page.getByRole('dialog', { name: 'Add a batch' });
  await add.getByLabel('Item').fill('TS');
  await add.getByRole('button', { name: /TS-100-M · Cotton T-shirt, medium · MAIN/ }).click();
  await add.getByLabel('Batch number').fill('lot-1');
  await add.getByLabel('Expires').fill('2027-01-31');
  await add.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByText(/Batch LOT-1 of MK-500 is saved/)).toBeVisible();
  expect(api.last('POST', '/api/batches')?.body).toMatchObject({ itemId: 'i-1', batchNumber: 'lot-1', expiresOn: '2027-01-31' });
});

test('bins are listed per location, and the lists come sorted by bin', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/bins/');
  await expect(page.getByRole('row', { name: /A-01.*Aisle 1/ })).toBeVisible();
  expect(api.last('GET', '/api/bins')?.query.get('locationId')).toBe('l-main-pos');

  await page.getByRole('button', { name: /New bin/ }).click();
  const add = page.getByRole('dialog', { name: 'New bin' });
  await add.getByLabel('Code').fill('b-07');
  await add.getByLabel('Zone').fill('Back room');
  await add.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByText('Bin B-07 is saved.')).toBeVisible();
  expect(api.last('POST', '/api/bins')?.body).toMatchObject({ locationId: 'l-main-pos', code: 'b-07', zone: 'Back room', isActive: true });

  // A bin in use is not deleted: the server says why.
  await page.getByRole('row', { name: /A-01/ }).click();
  await page.getByRole('button', { name: /^Delete/ }).click();
  await page.getByRole('dialog', { name: 'Delete bin A-01?' }).getByRole('button', { name: /^Delete/ }).click();
  await expect(page.getByText('12 items are kept in bin A-01; move them first, or make the bin inactive.')).toBeVisible();

  await page.getByRole('button', { name: /Pick list: customer orders/ }).click();
  const pick = page.getByRole('dialog', { name: 'Pick list: customer orders' });
  await expect(pick.getByRole('cell', { name: 'OAT-0901' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(pick).toBeHidden();

  await page.getByRole('button', { name: /Count sheet of A-01/ }).click();
  await expect(page.getByRole('dialog', { name: 'Count sheet of bin A-01' }).getByRole('cell', { name: 'TS-100-M' })).toBeVisible();
  expect(api.last('GET', '/api/bins/count-sheet')?.query.get('bin')).toBe('A-01');
});

test('items are put in a bin', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/bins/');
  await page.getByRole('button', { name: /Put items in a bin/ }).click();
  const assign = page.getByRole('dialog', { name: 'Put items in a bin' });
  await assign.getByLabel('Bin', { exact: true }).selectOption('C-02');
  await assign.getByLabel('Items', { exact: true }).fill('jeans');
  await assign.getByLabel(/JN-220-32 · Slim jeans 32/).check();
  await assign.getByRole('button', { name: /Put 1 items in C-02/ }).click();
  await expect(page.getByText('1 items are now kept in C-02.')).toBeVisible();
  expect(api.last('POST', '/api/bins/assign')?.body).toEqual({ itemIds: ['i-2'], binCode: 'C-02' });
});

test('held bill lines are accepted by someone who may approve, and the reports list what is not matched', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/matching/');
  const held = page.getByRole('row', { name: /Held.*INV-5531/ });
  await expect(held).toBeVisible();
  await held.click();
  const panel = page.getByRole('dialog', { name: 'Bill INV-5531, TS-100-M' });
  await expect(panel.getByText('Billed at 7.10 each for 6.40 ordered.')).toBeVisible();
  await panel.getByRole('button', { name: /^Accept/ }).click();
  const decide = page.getByRole('dialog', { name: 'Accept the bill line' });
  await decide.getByLabel('Note (optional)').fill('New price list');
  await decide.getByRole('button', { name: /^Accept/ }).click();
  await expect(page.getByText('The bill line is accepted.')).toBeVisible();
  expect(api.last('POST', '/api/matching/m-1/accept')?.body).toEqual({ version: 'v-1', note: 'New price list' });
  await expect(panel.getByText('Accepted').first()).toBeVisible();
  await panel.getByRole('button', { name: /^Close/ }).click();

  await page.getByRole('button', { name: /Check the bills again/ }).click();
  await expect(page.getByText('2 bill lines checked: 1 matched, 1 held, 1 price differences posted.')).toBeVisible();

  await page.getByRole('tab', { name: 'Received, not billed' }).click();
  await expect(page.getByRole('row', { name: /10000004.*Canvas sneakers/ })).toBeVisible();
  await page.getByRole('tab', { name: 'Billed, not received' }).click();
  await expect(page.getByRole('row', { name: /10000003.*Cotton T-shirt/ })).toBeVisible();

  await page.getByRole('button', { name: /Tolerances/ }).click();
  const tolerances = page.getByRole('dialog', { name: 'Match tolerances' });
  await tolerances.getByLabel('Price tolerance (%)').fill('3');
  await tolerances.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => (api.last('PUT', '/api/supply/settings')?.body as { priceTolerancePercent?: number })?.priceTolerancePercent).toBe(3);
});

test('without purchasing.approve a held line cannot be decided', async ({ page }) => {
  await mockApi(page, { user: buyer });
  await page.goto('/matching/');
  await page.getByRole('row', { name: /Held.*INV-5531/ }).click();
  const panel = page.getByRole('dialog', { name: 'Bill INV-5531, TS-100-M' });
  await expect(panel.getByText('Someone who may approve purchases accepts or rejects a held line.')).toBeVisible();
  await expect(panel.getByRole('button', { name: /^Accept/ })).toHaveCount(0);
  await expect(panel.getByRole('button', { name: /^Reject/ })).toHaveCount(0);
});

test('a landed cost voucher is made over received orders, previewed and posted', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/landed-costs/');
  await expect(page.getByRole('row', { name: /FR-881/ })).toBeVisible();
  await page.getByRole('button', { name: /New voucher/ }).click();
  const editor = page.getByRole('dialog', { name: 'New landed cost voucher' });
  await editor.getByLabel('Reference').fill('FR-990');
  await editor.getByLabel('Amount of charge 1').fill('45');
  await editor.getByRole('button', { name: /Save and preview/ }).click();
  await expect(editor.getByText('Choose the received orders the charges are for.')).toBeVisible();
  await editor.getByLabel(/Order 10000001 · Cotton Co. Ltd/).check();
  await editor.getByRole('button', { name: /Save and preview/ }).click();
  expect(api.last('POST', '/api/landed-costs')?.body).toMatchObject({
    reference: 'FR-990', basis: 'Value', storeId: 's-main', purchaseOrderIds: ['po-1'], charges: [{ kind: 'Freight', description: null, amount: 45 }],
  });

  const panel = page.getByRole('dialog', { name: 'Voucher 100000000003' });
  await expect(panel.getByText('How it would be spread now')).toBeVisible();
  await expect(panel.getByRole('cell', { name: '$37.21' })).toBeVisible();
  await panel.getByRole('button', { name: /^Post/ }).click();
  await expect(page.getByText('Voucher 100000000003 is posted: the costs of the stock on hand are raised.')).toBeVisible();
  await expect(panel.getByText('How it was spread')).toBeVisible();
});

test('only someone who may approve purchases makes landed cost vouchers', async ({ page }) => {
  await mockApi(page, { user: buyer });
  await page.goto('/landed-costs/');
  await expect(page.getByRole('row', { name: /FR-881/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /New voucher/ })).toHaveCount(0);
  await page.getByRole('row', { name: /FR-881/ }).click();
  await expect(page.getByRole('dialog', { name: /Voucher 100000000002/ }).getByRole('button', { name: /^Post/ })).toHaveCount(0);
});

test('reorder suggestions become purchase orders or a requisition', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/reorder/');
  await expect(page.getByRole('row', { name: /TS-100-M/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /SK-001/ })).toHaveCount(0);
  await expect(page.getByLabel('Order TS-100-M')).toBeChecked();
  await page.getByLabel('Pieces of SN-410').fill('9');
  await page.getByRole('button', { name: /Make 2 purchase orders/ }).click();
  await expect(page.getByText('2 draft purchase orders are made.')).toBeVisible();
  expect(api.last('POST', '/api/reorder/orders')?.body).toEqual({
    locationId: 'l-main-pos',
    lines: [
      { itemId: 'i-1', pieces: 24, supplierId: 'sp-1' },
      { itemId: 'i-3', pieces: 9, supplierId: 'sp-2' },
    ],
  });

  await page.getByLabel('Order SN-410').uncheck();
  await page.getByRole('button', { name: /Make a requisition/ }).click();
  await expect(page.getByText('Requisition 10000004 is made and sent for approval.')).toBeVisible();
  expect(api.last('POST', '/api/reorder/requisition')?.body).toMatchObject({ submit: true, lines: [{ itemId: 'i-1', pieces: 24, supplierId: 'sp-1' }] });
  await page.getByRole('link', { name: 'Open it' }).click();
  await expect(page.getByRole('dialog', { name: 'Requisition 10000004' })).toBeVisible();

  await page.goto('/reorder/');
  await page.getByLabel('Items with nothing to order too').check();
  await expect(page.getByRole('row', { name: /SK-001/ })).toBeVisible();
});

test('supplier performance shows on-time delivery, fill rate, cost changes and returns', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/supplier-performance/');
  const cotton = page.getByRole('row', { name: /Cotton Co. Ltd/ });
  await expect(cotton.getByText('75.0%')).toBeVisible();
  await expect(cotton.getByText('98.0%')).toBeVisible();
  await expect(cotton.getByText('4.69%')).toBeVisible();
  const asked = api.last('GET', '/api/supplier-performance');
  expect(asked?.query.get('from')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  expect(asked?.query.get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
});
