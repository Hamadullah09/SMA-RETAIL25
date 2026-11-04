import { expect, test } from '@playwright/test';
import { supervisor } from './fixtures';
import { mockApi } from './mock-api';

test('the Manufacturing group is in the navigation and Ctrl+K for roles with production, and not for others', async ({ page }) => {
  await mockApi(page);
  await page.goto('/production/');
  const nav = page.getByRole('navigation', { name: 'Pages' });
  await expect(nav.getByText('Manufacturing')).toBeVisible();
  for (const name of ['Production orders', 'Bills of materials', 'Routings', 'Work centres', 'Planning', 'Capacity', 'Quality', 'Production reports'])
    await expect(nav.getByRole('link', { name: new RegExp(`^${name}`) })).toBeVisible();
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Go to' });
  await palette.getByRole('combobox').fill('manufacturing');
  await expect(palette.getByRole('option').first()).toHaveText(/Go to Production orders/);
  await palette.getByRole('combobox').fill('capacity load');
  await expect(palette.getByRole('option').first()).toHaveText(/Go to Capacity/);
  await page.keyboard.press('Escape');

  await mockApi(page, { user: supervisor });
  await page.goto('/');
  await expect(page.getByRole('navigation', { name: 'Pages' }).getByRole('link', { name: /^Dashboard/ })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Pages' }).getByText('Manufacturing')).toHaveCount(0);
  await page.goto('/boms/');
  await expect(page.getByText('Your role cannot open this page.')).toBeVisible();
});

test('the board shows the orders by status, and a new order is planned from it', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/production/');
  const released = page.getByRole('region', { name: 'Released' });
  await expect(released.getByRole('link', { name: /Order 100000001/ })).toBeVisible();
  await expect(released.getByText('Priority 1')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Planned' }).getByText('To order')).toBeVisible();

  await page.getByRole('button', { name: /New order/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New production order' });
  await dialog.getByLabel('Product').fill('cake');
  await dialog.getByLabel('Quantity').fill('12');
  await dialog.getByLabel('Made for').selectOption('co-1');
  await dialog.getByRole('button', { name: /Plan the order/ }).click();
  await expect(page).toHaveURL(/\/production\/view\/\?id=o-2$/);
  const sent = api.last('POST', '/api/production-orders')!.body as Record<string, unknown>;
  expect(sent).toMatchObject({ productCode: 'CAKE', quantity: 12, materialLocationId: 'l-main-pos', outputLocationId: 'l-main-pos', customerOrderId: 'co-1', priority: 5 });

  await page.goto('/production/');
  await page.getByRole('button', { name: /Post costs to the ledger/ }).click();
  await expect(page.getByText('The costs of 3 order(s) are posted to the ledger.')).toBeVisible();
});

test('an order is released, worked and received from its page', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/production/view/?id=o-2');
  await expect(page.getByRole('heading', { name: 'Order 100000002' })).toBeVisible();
  await page.getByRole('button', { name: /Release to the floor/ }).click();
  const shortage = page.getByRole('dialog', { name: 'Released, with material short' });
  await expect(shortage.getByRole('row', { name: /SUGAR/ })).toContainText('1.4');
  await shortage.getByRole('button', { name: 'OK' }).click();

  await page.goto('/production/view/?id=o-1');
  await expect(page.getByRole('row', { name: /FLOUR/ })).toContainText('10');
  await page.getByLabel('Material code or barcode').fill('0400000000015');
  await page.getByLabel('Quantity').fill('2.5');
  await page.getByRole('button', { name: /^Issue$/ }).click();
  await expect.poll(() => api.last('POST', '/api/production-orders/o-1/issue')?.body).toEqual({ code: '0400000000015', quantity: 2.5, note: null });

  await page.getByRole('tab', { name: 'Operations' }).click();
  await page.getByRole('button', { name: 'Start step 10' }).click();
  await expect.poll(() => api.last('POST', '/api/production-orders/o-1/operations/10/start')).toBeTruthy();
  await page.getByRole('button', { name: 'Report step 10' }).click();
  const report = page.getByRole('dialog', { name: /Report step 10/ });
  await report.getByLabel('Minutes worked').fill('150');
  await report.getByLabel('Good quantity').fill('19');
  await report.getByLabel('Scrapped').fill('1');
  await report.getByRole('button', { name: /^Report/ }).click();
  await expect.poll(() => api.last('POST', '/api/production-orders/o-1/report')?.body).toEqual({ sequence: 10, minutes: 150, good: 19, scrap: 1, note: null });

  await page.getByRole('tab', { name: 'Output and quality' }).click();
  await page.getByLabel('Quantity made').fill('19');
  await page.getByText('This is the last of it: complete the order').click();
  await page.getByLabel('Crumb bag (by-product)').fill('2');
  await page.getByRole('button', { name: /Receive and complete/ }).click();
  await expect.poll(() => api.last('POST', '/api/production-orders/o-1/receive')?.body).toEqual({ quantity: 19, complete: true, byProducts: [{ stockCode: 'CRUMB', quantity: 2 }] });
  await expect(page.getByText('Completed').first()).toBeVisible();

  await page.getByRole('tab', { name: 'Costs' }).click();
  await expect(page.getByRole('table', { name: 'Ledger entries' })).toContainText('material issued');
  await expect(page.getByRole('table', { name: 'Material usage and price' })).toContainText('FLOUR');
});

test('an output inspection sends the answers, and failed goods go to a hold location', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/production/view/?id=o-4');
  await page.getByRole('tab', { name: 'Output and quality' }).click();
  await expect(page.getByRole('table', { name: 'Inspections of the output' })).toContainText('On hold at HOLD');
  await page.getByRole('button', { name: /Inspect output/ }).click();
  const dialog = page.getByRole('dialog', { name: /Inspect order/ });
  await dialog.getByLabel('Failed', { exact: true }).fill('1');
  await dialog.getByLabel('Hold location').selectOption('l-west-back');
  await dialog.getByLabel('Weight').fill('1010');
  await dialog.getByRole('button', { name: /Record the inspection/ }).click();
  await expect.poll(() => api.last('POST', '/api/production-orders/o-4/inspect')?.body).toMatchObject({
    quantityInspected: 10, quantityFailed: 1, disposition: 'Hold', holdLocationId: 'l-west-back',
    answers: [{ lineNumber: 1, number: 1010 }, { lineNumber: 2, passFail: false }],
  });
});

test('a bill shows its components, tree and cost, and saves with its version', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/boms/');
  await expect(page.getByRole('row', { name: /GIFT/ })).toBeVisible();
  await page.getByRole('row', { name: /CAKE.*In force/ }).click();
  await expect(page).toHaveURL(/\/boms\/edit\/\?id=b-1$/);
  await expect(page.getByLabel('Component 1 stock code')).toHaveValue('FLOUR');
  await page.getByLabel('Component 2 quantity').fill('2.5');
  await page.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/boms/b-1')?.body).toMatchObject({ version: 'v-2', yieldQuantity: 10, lines: [{ componentCode: 'FLOUR' }, { componentCode: 'SUGAR', quantity: 2.5 }, { componentCode: 'BOX', issueMethod: 'Backflush' }] });

  // A wrong line is stopped before it is sent.
  await page.getByLabel('Component 2 quantity').fill('0');
  const puts = api.requests.filter((r) => r.method === 'PUT').length;
  await page.getByRole('button', { name: /^Save/ }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'SUGAR: the quantity must be above zero.' }).first()).toBeVisible();
  expect(api.requests.filter((r) => r.method === 'PUT').length).toBe(puts);

  await page.getByRole('tab', { name: 'Tree' }).click();
  const tree = page.getByRole('table', { name: 'Exploded bill' });
  await expect(tree.getByRole('row', { name: /SUGAR/ })).toBeVisible();
  await tree.getByRole('button', { name: 'Hide the lines under CAKE' }).click();
  await expect(tree.getByRole('row', { name: /SUGAR/ })).toHaveCount(0);

  await page.getByRole('tab', { name: 'Cost' }).click();
  await expect(page.getByText('One unit (90% of the cost)')).toBeVisible();
  await page.getByLabel('Lot quantity').fill('20');
  await expect.poll(() => api.last('GET', '/api/boms/b-1/cost')?.query.get('quantity')).toBe('20');

  await page.getByRole('tab', { name: 'Versions' }).click();
  await expect(page.getByRole('table', { name: 'Differences' })).toContainText('Added');
  await page.getByRole('button', { name: /Copy as a new version/ }).click();
  await expect(page).toHaveURL(/\/boms\/edit\/\?id=b-copy$/);
});

test('a routing is changed on the Routings page', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/routings/');
  await page.getByRole('row', { name: /CAKE.*In force/ }).click();
  const routing = page.getByRole('region', { name: 'Routing of CAKE' });
  await expect(routing.getByLabel('Operation 1 name')).toHaveValue('Mix and bake');
  await routing.getByRole('button', { name: /Add operation/ }).click();
  await routing.getByLabel('Operation 3 name').fill('Label');
  await routing.getByLabel('Operation 3 run').fill('0.5');
  await routing.getByRole('button', { name: /Save routing/ }).click();
  await expect.poll(() => (api.last('PUT', '/api/boms/b-1/routing')?.body as { operations: unknown[] } | undefined)?.operations?.length).toBe(3);
  expect(api.last('PUT', '/api/boms/b-1/routing')?.body).toMatchObject({ version: 'v-2', operations: [{}, {}, { sequence: 30, name: 'Label', runMinutesPerUnit: 0.5, workCentreId: 'w-1' }] });
});

test('a work centre is added with its rates', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/work-centres/');
  await expect(page.getByRole('row', { name: /OVEN.*Deck oven/ })).toBeVisible();
  await page.getByRole('button', { name: /New work centre/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New work centre' });
  await dialog.getByLabel('Location').selectOption('l-main-pos');
  await dialog.getByLabel('Code').fill('mix');
  await dialog.getByLabel('Name').fill('Mixer');
  await dialog.getByLabel('Labour an hour').fill('55');
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/work-centres')?.body).toMatchObject({ code: 'MIX', name: 'Mixer', labourCostPerHour: 55, locationId: 'l-main-pos', capacityHoursPerDay: 8 });
});

test('planning runs, and chosen suggestions are firmed with a changed quantity', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/planning/');
  await expect(page.getByRole('row', { name: /SUGAR/ })).toContainText('Sweet Supplies');
  await page.getByLabel('Days ahead').fill('30');
  await page.getByRole('button', { name: /Run planning/ }).click();
  await expect.poll(() => api.last('POST', '/api/mrp/run')?.body).toMatchObject({ horizonDays: 30, defaultLeadDays: 7 });
  await expect(page.getByText(/Planning suggests 1 to make and 2 to buy/)).toBeVisible();

  await page.getByLabel('Choose CAKE').check();
  await page.getByLabel('Choose SUGAR').check();
  await page.getByLabel('Quantity of SUGAR').fill('20');
  await page.getByRole('button', { name: /^Firm/ }).click();
  await expect.poll(() => api.last('POST', '/api/mrp/firm')?.body).toEqual({ ids: ['sg-1', 'sg-2'], quantities: { 'sg-2': 20 } });
  await expect(page.getByText('Production order 100000009: 4 x CAKE')).toBeVisible();

  await page.getByRole('tab', { name: 'Supplier lead times' }).click();
  await page.getByLabel('Lead time of Sweet Supplies').fill('4');
  await page.getByRole('button', { name: /Save lead times/ }).click();
  await expect.poll(() => api.last('PUT', '/api/mrp/lead-times/sup-2')?.body).toEqual({ days: 4 });
});

test('capacity shows each work centre by week, an overloaded week in danger', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/capacity/');
  const grid = page.getByRole('table', { name: 'Capacity load' });
  await expect(grid.getByRole('row', { name: /OVEN Deck oven/ })).toContainText('108%');
  await expect(grid.getByRole('row', { name: /PACK Packing bench/ })).toContainText('42 of 48 h');
  await page.getByText('With what planning suggests to make').click();
  await expect.poll(() => api.last('GET', '/api/capacity')?.query.get('suggestions')).toBe('true');
});

test('quality plans, inspections and goods received are kept on the Quality page', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/quality/');
  await expect(page.getByRole('row', { name: /CAKE.*Cake checks/ })).toBeVisible();
  await page.getByRole('button', { name: /New plan/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New quality plan' });
  await dialog.getByLabel('Product').fill('bread');
  await dialog.getByLabel('Name', { exact: true }).fill('Bread checks');
  await dialog.getByLabel('Check 1 name').fill('Crust colour');
  await dialog.getByLabel('Check 1 kind').selectOption('Text');
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/quality/plans')?.body).toMatchObject({ productCode: 'BREAD', name: 'Bread checks', checks: [{ lineNumber: 1, name: 'Crust colour', kind: 'Text' }] });

  await page.getByRole('tab', { name: 'Inspections' }).click();
  await page.getByRole('row', { name: /Production order 100000004/ }).click();
  await expect(page.getByRole('dialog', { name: 'Inspection of CAKE' })).toContainText('held at HOLD');
  await page.keyboard.press('Escape');

  await page.getByRole('tab', { name: 'Goods received' }).click();
  await page.getByRole('button', { name: 'Inspect SUGAR of purchase order 10000031' }).click();
  const incoming = page.getByRole('dialog', { name: /Inspect SUGAR/ });
  await incoming.getByText('Bags sealed').click();
  await incoming.getByRole('button', { name: /Record the inspection/ }).click();
  await expect.poll(() => api.last('POST', '/api/quality/receipts/po-1/inspect')?.body).toMatchObject({ stockCode: 'SUGAR', quantityInspected: 25, answers: [{ lineNumber: 1, passFail: true }] });
});

test('the production reports list and run', async ({ page }) => {
  await mockApi(page);
  await page.goto('/production-reports/');
  await page.getByRole('button', { name: /Production orders by status/ }).click();
  await expect(page).toHaveURL(/key=production-status/);
  const table = page.getByRole('table', { name: 'Production orders by status' });
  await expect(table.getByRole('row', { name: /in progress/ })).toBeVisible();
  await expect(table).toContainText('Total');
});

test('the keys on the buttons work: F9 releases, Insert adds a line, Del dismisses and deletes', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/production/view/?id=o-2');
  await expect(page.getByRole('heading', { name: 'Order 100000002' })).toBeVisible();
  await page.keyboard.press('F9');
  await expect(page.getByRole('dialog', { name: 'Released, with material short' })).toBeVisible();
  expect(api.last('POST', '/api/production-orders/o-2/release')).toBeTruthy();
  await page.keyboard.press('Enter');

  await page.goto('/boms/edit/?id=b-1');
  await expect(page.getByLabel('Component 3 stock code')).toHaveValue('BOX');
  await page.keyboard.press('Insert');
  await expect(page.getByLabel('Component 4 stock code')).toBeVisible();

  await page.goto('/planning/');
  await page.getByLabel('Choose BOX').check();
  await page.keyboard.press('Delete');
  await expect.poll(() => api.last('POST', '/api/mrp/dismiss')?.body).toEqual({ ids: ['sg-3'] });

  await page.goto('/work-centres/');
  await page.getByRole('row', { name: /PACK.*Packing bench/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Change PACK' });
  await dialog.getByRole('button', { name: /^Delete/ }).click();
  await page.getByRole('dialog', { name: 'Delete PACK?' }).getByRole('button', { name: /^Yes/ }).click();
  await expect.poll(() => api.last('DELETE', '/api/work-centres/w-2')).toBeTruthy();
});
