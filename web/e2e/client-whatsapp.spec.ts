import { expect, test } from '@playwright/test';
import { clientWithWhatsApp, supervisor } from './fixtures';
import { mockApi } from './mock-api';

test("a client's WhatsApp number and consent are kept, and a message goes from a template", async ({ page }) => {
  const api = await mockApi(page, { overrides: { 'GET /api/clients/cl-1': () => ({ json: clientWithWhatsApp() }) } });
  await page.goto('/clients/');
  await page.getByRole('row', { name: /Akua Owusu/ }).dblclick();
  const dialog = page.getByRole('dialog', { name: /Client 10000001/ });
  const number = dialog.getByLabel('WhatsApp', { exact: true });
  const consent = dialog.getByLabel('Agrees to WhatsApp messages');
  await expect(number).toHaveValue('+14165550142');
  await expect(consent).toBeChecked();
  await expect(dialog.getByText(/^Agreed /)).toBeVisible();
  await expect(dialog.getByLabel('Birthday')).toHaveValue('1990-03-14');
  await expect(dialog.getByRole('list', { name: 'Messages sent' })).toContainText('Hi Akua, our spring sale at Downtown starts Saturday: 20% off.');

  await dialog.getByRole('button', { name: /Send on WhatsApp/ }).click();
  const send = page.getByRole('dialog', { name: 'WhatsApp to Akua Owusu' });
  await expect(send.getByLabel('Template').locator('option')).toHaveText(['Choose…', 'spring_sale (en)', 'points_reminder (en)']);
  await send.getByLabel('Template').selectOption({ label: 'points_reminder (en)' });
  await expect(send.getByTestId('send-preview')).toHaveText('Hi Akua, you have 320 points to spend.');
  await send.getByRole('button', { name: /^Send/ }).click();
  await expect.poll(() => api.last('POST', '/api/clients/cl-1/whatsapp')?.body).toEqual({ templateId: 't-2', storeId: 's-main' });
  await expect(page.getByText('Sent to +14165550142.')).toBeVisible();

  // A local number is refused; taking the number away takes the consent with it.
  await number.fill('0300 1234567');
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect(dialog.getByText('Type the number with its country code, like +923001234567.')).toBeVisible();
  await number.fill('');
  await expect(consent).not.toBeChecked();
  await expect(consent).toBeDisabled();
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/clients/cl-1')?.body).toMatchObject({ whatsAppNumber: null, whatsAppOptIn: false, birthday: '1990-03-14' });
});

test('consent needs a number, and a client without it cannot be sent a message', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/clients/');
  await page.getByRole('row', { name: /Daniel Kim/ }).dblclick();
  const dialog = page.getByRole('dialog', { name: /Client 10000002/ });
  await expect(dialog.getByRole('button', { name: /Send on WhatsApp/ })).toBeDisabled();
  await expect(dialog.getByText('No WhatsApp number yet.')).toBeVisible();
  await expect(dialog.getByLabel('Agrees to WhatsApp messages')).toBeDisabled();
  await dialog.getByLabel('WhatsApp', { exact: true }).fill('+1 416 555 0199');
  await dialog.getByLabel('Agrees to WhatsApp messages').check();
  await dialog.getByRole('button', { name: /^Save/ }).click();
  await expect.poll(() => api.last('PUT', '/api/clients/cl-2')?.body).toMatchObject({ whatsAppNumber: '+14165550199', whatsAppOptIn: true });
});

test('a client who said no at the till is shown as such', async ({ page }) => {
  const saidNo = { ...clientWithWhatsApp(), whatsAppOptIn: false, whatsAppOptInAtUtc: null };
  await mockApi(page, { overrides: { 'GET /api/clients/cl-1': () => ({ json: saidNo }) } });
  await page.goto('/clients/');
  await page.getByRole('row', { name: /Akua Owusu/ }).dblclick();
  const dialog = page.getByRole('dialog', { name: /Client 10000001/ });
  await expect(dialog.getByLabel('Agrees to WhatsApp messages')).not.toBeChecked();
  await expect(dialog.getByText(/^Said no .* Nothing is sent to them\.$/)).toBeVisible();
});

test('without the CRM a client has no WhatsApp section', async ({ page }) => {
  await mockApi(page, { user: supervisor });
  await page.goto('/clients/');
  await page.getByRole('row', { name: /Akua Owusu/ }).dblclick();
  const dialog = page.getByRole('dialog', { name: /Client 10000001/ });
  await expect(dialog.getByLabel('First name')).toBeVisible();
  await expect(dialog.getByRole('heading', { name: 'WhatsApp messages' })).toHaveCount(0);
});
