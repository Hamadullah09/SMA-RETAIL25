import { describe, expect, it } from 'vitest';
import { readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ROUTES, NAV_SECTIONS, PALETTE_ROUTES, childrenOf } from './routes';
import { activeNavHref, breadcrumbFor, helpTopicFor, matchRoute } from './route-match';

/**
 * The registry against the file tree.
 *
 * This exists because three hand-maintained lists drifted apart without anyone noticing, and the
 * only reliable witness to what screens exist is the app directory itself. If a route is added and
 * not declared here, it is unreachable from the rail, the palette and Ctrl+H at once — which is
 * exactly how seven of the nine reports came to be missing from the palette.
 */
const APP_DIR = fileURLToPath(new URL('../app/(dashboard)', import.meta.url));

function routesOnDisk(dir: string, prefix = ''): string[] {
  const found: string[] = [];

  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);

    if (entry === 'page.tsx') {
      found.push(prefix === '' ? '/' : prefix);
      continue;
    }

    if (statSync(path).isDirectory()) {
      // Route groups like (dashboard) do not appear in the URL.
      const segment = entry.startsWith('(') ? '' : `/${entry}`;
      found.push(...routesOnDisk(path, prefix + segment));
    }
  }

  return found;
}

const ON_DISK = routesOnDisk(APP_DIR).filter((r) => r !== '/');

describe('the registry covers the application', () => {
  it('declares every page that exists', () => {
    const declared = new Set(ROUTES.map((r) => r.href));
    const missing = ON_DISK.filter((href) => !declared.has(href));

    expect(missing, 'these pages exist but are unreachable from the rail, the palette and help').toEqual([]);
  });

  it('declares nothing that does not exist', () => {
    const onDisk = new Set(ON_DISK);
    const dangling = [...new Set(ROUTES.map((r) => r.href))].filter((href) => !onDisk.has(href));

    expect(dangling, 'these are declared but have no page — a dead link in the rail').toEqual([]);
  });
});

describe('one word means one destination', () => {
  /**
   * The bug this file was written for. "Inventory" pointed at /catalog/products in the rail and at
   * /inventory in the palette — opposite screens, same word, and both pages headed "Inventory".
   */
  it('never gives two different destinations the same label', () => {
    const byLabel = new Map<string, Set<string>>();

    for (const route of ROUTES) {
      const hrefs = byLabel.get(route.label) ?? new Set<string>();
      hrefs.add(route.href);
      byLabel.set(route.label, hrefs);
    }

    const ambiguous = [...byLabel.entries()]
      .filter(([, hrefs]) => hrefs.size > 1)
      .map(([label, hrefs]) => `${label} -> ${[...hrefs].join(' and ')}`);

    expect(ambiguous).toEqual([]);
  });
});

describe('the palette can reach everything', () => {
  it.each([
    '/dashboard',
    '/sales',
    '/orders',
    '/admin/backup',
    '/admin/accounting',
    '/reports/tax',
    '/reports/stock-value',
    '/reports/on-order',
    '/reports/reward-points',
    '/reports/sales-analysis',
    '/reports/stock-position',
    '/reports/stock-received',
  ])('%s is reachable', (href) => {
    expect(PALETTE_ROUTES.some((r) => r.href === href)).toBe(true);
  });
});

describe('matching a pathname', () => {
  it('prefers the longest prefix, so only one row is current', () => {
    expect(matchRoute('/purchasing/suppliers')?.href).toBe('/purchasing/suppliers');
    expect(matchRoute('/inventory/counts')?.href).toBe('/inventory/counts');
  });

  it('lights the parent row for a child page', () => {
    expect(activeNavHref('/inventory/counts')).toBe('/inventory');
    expect(activeNavHref('/reports/tax')).toBe('/reports');
    expect(activeNavHref('/dashboard')).toBe('/dashboard');
  });

  it('matches a deeper path than any declared route', () => {
    expect(matchRoute('/customers/42/edit')?.href).toBe('/customers');
  });

  it('returns nothing for an unknown path rather than guessing', () => {
    expect(matchRoute('/nowhere')).toBeUndefined();
  });
});

describe('help resolution', () => {
  it.each([
    ['/pos', '/help/pos'],
    ['/inventory', '/help/inventory'],
    ['/catalog/products', '/help/products'],
    ['/inventory/counts', '/help/counts'],
  ])('%s opens %s', (path, expected) => {
    expect(helpTopicFor(path)).toBe(expected);
  });

  it('falls back to the index rather than opening the wrong guide', () => {
    expect(helpTopicFor('/nowhere')).toBe('/help');
  });
});

describe('breadcrumbs', () => {
  it('names the parent and the page for a child route', () => {
    expect(breadcrumbFor('/inventory/counts').map((r) => r.label)).toEqual(['Stock', 'Stock counts']);
  });

  it('is a single entry at the top level', () => {
    expect(breadcrumbFor('/customers').map((r) => r.label)).toEqual(['Customers']);
  });
});

describe('the rail', () => {
  it('puts every section in order with items in it', () => {
    expect(NAV_SECTIONS.map((s) => s.heading)).toEqual(['Main', 'Operations', 'Others']);

    for (const section of NAV_SECTIONS) {
      expect(section.items.length, `${section.heading} is empty`).toBeGreaterThan(0);
    }
  });

  it('gives Stock and Inventory their own children', () => {
    expect(childrenOf('/inventory').map((r) => r.href)).toEqual([
      '/inventory',
      '/inventory/counts',
      '/inventory/transfers',
    ]);
    expect(childrenOf('/catalog/products').map((r) => r.href)).toEqual([
      '/catalog/products',
      '/catalog/bulk',
    ]);
  });
});
