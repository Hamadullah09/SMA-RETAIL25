import { expect, test } from '@playwright/test';
import { mockApi } from './mock-api';

test('uploading a package from a USB stick shows progress and the result', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/packages/');
  await expect(page.getByRole('cell', { name: 'WEST-012-usb.smapkg' })).toBeVisible();
  const before = api.requests.filter((r) => r.path === '/api/sync/batches').length;

  await page.getByLabel('Package file').setInputFiles({ name: 'MAIN-001-usb.smapkg', mimeType: 'application/octet-stream', buffer: Buffer.alloc(64 * 1024, 7) });

  await expect(page.getByTestId('drop-zone').getByText('The package went in: 380 rows added, 30 changed, 2 deleted.')).toBeVisible();
  await expect(page.getByTestId('drop-zone').getByText("2 clients were known already and kept the server's details.")).toBeVisible();
  await expect(page.getByRole('progressbar', { name: 'Upload' })).toHaveAttribute('aria-valuenow', '100');

  const upload = api.last('POST', '/api/sync/upload-file');
  expect(upload?.headers['x-requested-with']).toBe('retailplus-web');
  expect(upload?.headers['content-type']).toMatch(/^multipart\/form-data/);
  expect(String(upload?.body)).toContain('name="file"; filename="MAIN-001-usb.smapkg"');
  await expect.poll(() => api.requests.filter((r) => r.path === '/api/sync/batches').length).toBeGreaterThan(before);
});

test('a file that is not a package is refused before sending', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto('/packages/');
  await page.getByLabel('Package file').setInputFiles({ name: 'photo.jpg', mimeType: 'image/jpeg', buffer: Buffer.from('x') });
  await expect(page.getByText('Choose a .smapkg file')).toBeVisible();
  expect(api.last('POST', '/api/sync/upload-file')).toBeUndefined();
});

test("the server's reason for refusing a package is shown", async ({ page }) => {
  await mockApi(page, {
    overrides: { 'POST /api/sync/upload-file': () => ({ status: 404, json: { error: 'Station 9 has not joined this server, or was retired.' } }) },
  });
  await page.goto('/packages/');
  await page.getByLabel('Package file').setInputFiles({ name: 'OLD-009.smapkg', mimeType: 'application/octet-stream', buffer: Buffer.from('pk') });
  await expect(page.getByText('Station 9 has not joined this server, or was retired.')).toBeVisible();
});
