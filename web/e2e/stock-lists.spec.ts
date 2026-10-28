import { expect, test } from '@playwright/test';
import { transfers } from './fixtures';
import { mockApi } from './mock-api';

test('transfers and stock counts are paged and searched by number or location', async ({ page }) => {
  const many = Array.from({ length: 120 }, (_, i) => ({ ...transfers[0], id: `t-many-${i}`, number: 1000100 + i }));
  const api = await mockApi(page, {
    overrides: {
      'GET /api/transfers': (r) => {
        const pageNo = Number(r.query.get('page') ?? 1);
        const size = Number(r.query.get('pageSize') ?? 50);
        const found = r.query.get('search') ? many.filter((t) => String(t.number).includes(r.query.get('search')!)) : many;
        return { json: { items: found.slice((pageNo - 1) * size, pageNo * size), total: found.length, page: pageNo, pageSize: size } };
      },
    },
  });
  await page.goto('/stock/');
  await page.getByRole('tab', { name: 'Transfers' }).click();
  await expect(page.getByText('1–50 of 120 transfers')).toBeVisible();
  expect(api.last('GET', '/api/transfers')?.query.get('pageSize')).toBe('50');

  await page.getByRole('button', { name: 'Next page' }).click();
  await expect.poll(() => api.last('GET', '/api/transfers')?.query.get('page')).toBe('2');
  await expect(page.getByText('51–100 of 120 transfers')).toBeVisible();

  // A search starts again at the first page.
  await page.getByPlaceholder('Transfer number or location').fill('1000150');
  await expect.poll(() => api.last('GET', '/api/transfers')?.query.get('search')).toBe('1000150');
  expect(api.last('GET', '/api/transfers')?.query.get('page')).toBe('1');
  await expect(page.getByRole('row', { name: /1000150/ })).toBeVisible();
  await expect(page.getByText('1–1 of 1 transfers')).toBeVisible();

  // Counts the same way, searched by location.
  await page.getByRole('tab', { name: 'Stock counts' }).click();
  await expect(page.getByRole('row', { name: /2000002/ })).toBeVisible();
  await page.getByPlaceholder('Count number or location').fill('POS');
  await expect.poll(() => api.last('GET', '/api/stock-counts')?.query.get('search')).toBe('POS');
  await expect(page.getByText('1–2 of 2 counts')).toBeVisible();
});
