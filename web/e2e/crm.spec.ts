import { expect, test } from '@playwright/test';
import { crmViewer } from './fixtures';
import { mockApi } from './mock-api';

test('the pipeline shows each stage with its totals, and a card is dragged to another stage', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/');
  await expect(page.getByRole('heading', { name: 'CRM', level: 1 })).toBeVisible();

  const newStage = page.getByTestId('stage-New');
  const proposal = page.getByTestId('stage-Proposal');
  await expect(newStage.getByRole('heading', { name: 'New' })).toBeVisible();
  await expect(page.getByTestId('stage-value-New')).toHaveText('$1,200.00');
  await expect(page.getByTestId('pipeline-totals')).toContainText('3');
  await expect(page.getByTestId('pipeline-totals')).toContainText('$5,600.00');
  await expect(page.getByTestId('stage-Lost')).toContainText('Price');

  await page.getByTestId('card-o-1').dragTo(proposal);
  await expect.poll(() => api.last('POST', '/api/opportunities/o-1/move')?.body).toEqual({ stageId: 'st-proposal', version: 'v-1', lostReason: null });
  expect(api.last('POST', '/api/opportunities/o-1/move')?.headers['x-requested-with']).toBe('retailplus-web');
  await expect(proposal.getByTestId('card-o-1')).toBeVisible();
  await expect(page.getByTestId('stage-value-Proposal')).toHaveText('$2,100.00');
  await expect(page.getByText('Uniforms for Kim Design is in Proposal.')).toBeVisible();
});

test('a card moved to Lost asks why, and the Move to box works from the keyboard', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/');
  await page.getByLabel('Move Office party gifts to').selectOption({ label: 'Move to Lost' });
  const why = page.getByRole('dialog', { name: 'Why was Office party gifts lost?' });
  await why.getByRole('button', { name: /Mark lost/ }).click();
  await expect(why.getByText('Type the reason.')).toBeVisible();
  await why.getByLabel('Reason').fill('Went to a competitor');
  await why.getByRole('button', { name: /Mark lost/ }).click();
  await expect.poll(() => api.last('POST', '/api/opportunities/o-3/move')?.body).toEqual({ stageId: 'st-lost', version: 'v-1', lostReason: 'Went to a competitor' });
  await expect(page.getByTestId('stage-Lost').getByTestId('card-o-3')).toContainText('Went to a competitor');

  // Filters go to the server.
  await page.locator('#board-owner').selectOption({ label: 'Ama Darko' });
  await expect.poll(() => api.last('GET', '/api/opportunities/board')?.query.get('ownerId')).toBe('u-3');
  await page.locator('#board-dates').selectOption('custom');
  await page.getByLabel('Close date from').fill('2026-11-01');
  await page.getByLabel('Close date to').fill('2026-11-30');
  await expect.poll(() => api.last('GET', '/api/opportunities/board')?.query.get('to')).toBe('2026-11-30');
});

test('a new opportunity is for a client or lead, found by typing', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/?tab=pipeline&new=1');
  const dialog = page.getByRole('dialog', { name: 'New opportunity' });
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Give it a title.')).toBeVisible();
  await dialog.getByLabel('Title').fill('Team jerseys');
  await dialog.getByPlaceholder('Find a client by name').fill('Akua');
  await dialog.getByLabel('The client').selectOption({ label: 'Akua Owusu' });
  await dialog.getByLabel('Expected value').fill('750');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/opportunities')?.body).toMatchObject({ title: 'Team jerseys', clientId: 'cl-1', leadId: null, stageId: 'st-new', expectedValue: 750 });
});

test("saving a converted lead's opportunity keeps both its client and its lead", async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/');
  await page.getByTestId('card-o-1').getByRole('button', { name: 'Uniforms for Kim Design' }).click();
  const dialog = page.getByRole('dialog', { name: 'Uniforms for Kim Design' });
  await dialog.getByLabel('Expected value').fill('1300');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/opportunities/o-1')?.body).toMatchObject({ clientId: 'cl-2', leadId: 'l-3', expectedValue: 1300, version: 'v-1' });
});

test('leads are listed, added with a checked WhatsApp number, and made clients', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/');
  await page.getByRole('tab', { name: 'Leads' }).click();
  await expect(page.getByRole('row', { name: /Kwame Asante/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Daniel Kim/ })).toHaveCount(0);
  await page.locator('#lead-status-filter').selectOption('');
  await expect(page.getByRole('row', { name: /Daniel Kim/ })).toContainText('Client Daniel Kim');

  await page.getByRole('button', { name: /New lead/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New lead' });
  await dialog.getByLabel('Name').fill('Yaa Boateng');
  await dialog.getByLabel('WhatsApp').fill('0300 1234567');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Type the number with its country code, like +923001234567.')).toBeVisible();
  await dialog.getByLabel('WhatsApp').fill('+92 300 1234567');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/leads')?.body).toMatchObject({ name: 'Yaa Boateng', whatsAppNumber: '+923001234567', ownerId: 'u-1', storeId: 's-main' });

  await page.getByRole('row', { name: /Kwame Asante/ }).click();
  await page.getByRole('button', { name: /Make a client/ }).click();
  const convert = page.getByRole('dialog', { name: 'Make Kwame Asante a client' });
  await convert.getByRole('button', { name: /Make a client/ }).click();
  await expect.poll(() => api.last('POST', '/api/leads/l-1/convert')?.body).toEqual({ clientId: null, version: 'v-1' });
  await expect(page.getByText('Kwame Asante is now client 10000005.')).toBeVisible();
});

test('activities are filtered and ticked done; Won and lost adds up what closed', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/?tab=activities');
  await expect(page.getByRole('row', { name: /Call Daniel about the uniforms/ })).toContainText('Overdue');
  await expect(page.getByRole('row', { name: /Thank Akua for the order/ })).toHaveCount(0);
  await page.getByLabel('Call Daniel about the uniforms done').click();
  await expect.poll(() => api.last('POST', '/api/activities/a-1/done')?.body).toEqual({ done: true, version: 'v-1' });
  await page.locator('#act-state-filter').selectOption('done');
  await expect.poll(() => api.last('GET', '/api/activities')?.query.get('done')).toBe('true');

  await page.getByRole('tab', { name: 'Won and lost' }).click();
  await expect(page.getByTestId('figure-Won')).toHaveText('$640.00');
  await expect(page.getByTestId('figure-Lost')).toHaveText('$5,000.00');
  await expect(page.getByTestId('figure-Win rate')).toHaveText('50.0%');
  await expect(page.getByRole('row', { name: /Hotel staff wear/ })).toContainText('Price');
});

test('the stages are changed in one list', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/crm/');
  await page.getByRole('button', { name: /Stages/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Pipeline stages' });
  await dialog.getByRole('button', { name: /Add a stage/ }).click();
  await dialog.getByLabel('Stage 6 name').fill('Negotiation');
  await dialog.getByRole('button', { name: 'Move Negotiation up' }).click();
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => (api.last('PUT', '/api/crm/stages')?.body as Array<{ name: string }> | undefined)?.map((s) => s.name)).toEqual([
    'New',
    'Qualified',
    'Proposal',
    'Won',
    'Negotiation',
    'Lost',
  ]);
});

test('someone who may only read the CRM sees the board without moving it', async ({ page }) => {
  await mockApi(page, { user: crmViewer });
  await page.goto('/crm/');
  await expect(page.getByTestId('card-o-1')).toBeVisible();
  await expect(page.getByRole('button', { name: /New opportunity/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Stages/ })).toHaveCount(0);
  await expect(page.getByLabel('Move Uniforms for Kim Design to')).toHaveCount(0);
  await expect(page.getByTestId('card-o-1')).toHaveAttribute('draggable', 'false');
  await expect(page.getByText('Your role can read the pipeline but not change it.')).toBeVisible();
});
