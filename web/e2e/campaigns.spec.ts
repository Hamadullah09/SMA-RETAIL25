import { expect, test } from '@playwright/test';
import { marketer } from './fixtures';
import { mockApi } from './mock-api';

test('campaigns are listed, a draft is made and sent now, and a scheduled one is cancelled', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/campaigns/');
  await expect(page.getByTestId('whatsapp-banner')).toContainText('WhatsApp is not connected.');
  await expect(page.getByRole('row', { name: /Spring sale/ })).toContainText('Sent');
  await expect(page.getByRole('row', { name: /Spring sale/ })).toContainText('33 · 15');

  await page.getByRole('button', { name: /New campaign/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New campaign' });
  await dialog.getByRole('button', { name: /Save draft/ }).click();
  await expect(dialog.getByText('Give the campaign a name.')).toBeVisible();
  await dialog.getByLabel('Name').fill('Black Friday');
  await dialog.getByLabel('Segment').selectOption({ label: 'Big spenders' });
  await expect(dialog.getByText(/42 clients when last evaluated/)).toBeVisible();
  await expect(dialog.getByLabel('Template').locator('option')).toHaveText(['Choose…', 'spring_sale (en)', 'points_reminder (en)']);
  await dialog.getByLabel('Template').selectOption({ label: 'spring_sale (en)' });
  await dialog.getByRole('button', { name: /Save draft/ }).click();
  await expect.poll(() => api.last('POST', '/api/campaigns')?.body).toEqual({ name: 'Black Friday', segmentId: 'seg-1', templateId: 't-1', storeId: 's-main', version: null });

  // The draft is sent now.
  await page.getByRole('row', { name: /Points reminder/ }).click();
  await expect(page.getByRole('button', { name: /Cancel sending/ })).toBeDisabled();
  await page.getByRole('button', { name: /^Send/ }).click();
  const send = page.getByRole('dialog', { name: 'Send Points reminder' });
  await send.getByRole('button', { name: /Send now/ }).click();
  await expect.poll(() => api.last('POST', '/api/campaigns/cm-2/schedule')?.body).toEqual({ sendAtUtc: null, version: 'v-1' });
  await expect(page.getByText('Points reminder: 2 messages queued.')).toBeVisible();

  // A scheduled one can be cancelled, not changed.
  await page.getByRole('row', { name: /Weekend launch/ }).click();
  await expect(page.getByRole('button', { name: /^Edit/ })).toBeDisabled();
  await page.getByRole('button', { name: /Cancel sending/ }).click();
  const cancel = page.getByRole('dialog', { name: 'Cancel Weekend launch?' });
  await expect(cancel).toContainText('The 40 messages still queued are never sent.');
  await cancel.getByRole('button', { name: /Cancel the campaign/ }).click();
  await expect.poll(() => api.last('POST', '/api/campaigns/cm-3/cancel')?.body).toEqual({ version: 'v-1' });
});

test('a campaign can be scheduled for later', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/campaigns/');
  await page.getByRole('row', { name: /Points reminder/ }).click();
  await page.getByRole('button', { name: /^Send/ }).click();
  const send = page.getByRole('dialog', { name: 'Send Points reminder' });
  await send.getByLabel('Later').check();
  await send.getByLabel('Send on').fill('2099-01-15');
  await send.getByLabel('Send at').fill('09:30');
  await send.getByRole('button', { name: /Schedule/ }).click();
  await expect.poll(() => (api.last('POST', '/api/campaigns/cm-2/schedule')?.body as { sendAtUtc?: string } | undefined)?.sendAtUtc).toMatch(/^2099-01-15T\d\d:30:00Z$/);
});

test('templates are checked as WhatsApp checks them and previewed with sample values', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/campaigns/?tab=templates');
  await expect(page.getByRole('row', { name: /spring_sale/ })).toContainText('{FirstName} {Store}');
  await page.getByRole('button', { name: /New template/ }).click();
  const dialog = page.getByRole('dialog', { name: 'New template' });
  const message = dialog.getByLabel('Message', { exact: true });
  await dialog.getByLabel('Name at WhatsApp').fill('Black Friday');
  await message.fill('Hi {FirstName}, {Nickname}');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect(dialog.getByText('Lower-case letters, digits and _ only, like spring_sale.')).toBeVisible();
  await expect(dialog.getByText('Not a placeholder: {Nickname}.')).toBeVisible();

  await dialog.getByLabel('Name at WhatsApp').fill('black_friday');
  await message.fill('Hi ');
  await dialog.getByRole('button', { name: '{FirstName}' }).click();
  await expect(message).toHaveValue('Hi {FirstName}');
  await expect(dialog.getByTestId('template-preview')).toHaveText('Hi Akua');
  await message.fill('Hi {FirstName}, you have {Points} points at {Store}.');
  await expect(dialog.getByTestId('template-preview')).toHaveText('Hi Akua, you have 320 points at Downtown.');
  await expect(dialog).toContainText('Parameters in order: 1. {FirstName}, 2. {Points}, 3. {Store}.');
  await dialog.getByRole('button', { name: /Save/ }).click();
  await expect.poll(() => api.last('POST', '/api/whatsapp/templates')?.body).toEqual({
    name: 'black_friday',
    language: 'en',
    category: 'Marketing',
    body: 'Hi {FirstName}, you have {Points} points at {Store}.',
    isActive: true,
    version: null,
  });
});

test('a marketer of one store reads the templates without changing them', async ({ page }) => {
  await mockApi(page, { user: marketer });
  await page.goto('/campaigns/?tab=templates');
  await expect(page.getByRole('row', { name: /points_reminder/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /New template/ })).toHaveCount(0);
  await expect(page.getByText('Templates are shared by every store: a super administrator changes them.')).toBeVisible();
});
