import { expect, test, type Page } from '@playwright/test';

/**
 * No page may scroll sideways.
 *
 * The brief's rule is that a layout adapts rather than shrinks, and horizontal scrolling is the
 * single most legible symptom of it doing neither. One assertion — the document is no wider than
 * the window — catches every fixed-pixel width, every unwrapped flex row and every table that was
 * left to overflow, on every route, at every width, without anyone having to look.
 *
 * It runs at three widths because one width proves nothing: `desktop` is 1280, `tablet` 820 and
 * `phone` 390 (playwright.config.ts). The narrow projects run this directory only.
 *
 * Needs the stack running and a password:
 *   E2E_PASSWORD='…' npx playwright test layout
 *
 * To re-seed the baselines below after a fix, run with SEED_BASELINE=1 and paste what it prints:
 *   SEED_BASELINE=1 E2E_PASSWORD='…' npx playwright test layout
 */

const CREDENTIALS = {
  username: process.env.E2E_USERNAME ?? 'admin@retail25.local',
  password: process.env.E2E_PASSWORD ?? '',
};

/** Reported rather than asserted, so one run can print a whole baseline instead of stopping at the first failure. */
const SEEDING = process.env.SEED_BASELINE === '1';

/** Every route under app/, less the dynamic and auth-callback ones. Derived from the file tree. */
const PUBLIC_ROUTES = ['/', '/sign-up', '/sign-up/done', '/forgot-password', '/reset-password'];

const SIGNED_IN_ROUTES = [
  '/dashboard',
  '/pos',
  '/sales',
  '/orders',
  '/customers',
  '/receivables',
  '/inventory',
  '/inventory/counts',
  '/inventory/transfers',
  '/catalog/products',
  '/catalog/bulk',
  '/purchasing',
  '/purchasing/suppliers',
  '/reports',
  '/reports/sales',
  '/reports/sales-analysis',
  '/reports/stock-position',
  '/reports/stock-value',
  '/reports/stock-received',
  '/reports/on-order',
  '/reports/reward-points',
  '/reports/tax',
  '/admin',
  '/admin/settings',
  '/admin/staff',
  '/admin/audit',
  '/admin/accounting',
  '/admin/backup',
  '/admin/rfid',
  '/admin/migration',
  '/admin/undelete',
  '/admin/year-end',
];

/**
 * Routes known to overflow today, per project. These are documented failures, not accepted ones —
 * `test.fail()` means the suite is green while they are broken and turns red the moment one is
 * *fixed*, which is the prompt to delete it from this list. A new route appearing here is a
 * regression and fails immediately.
 *
 * The desktop list is computed, not guessed: it is every route whose widest DataGrid column set
 * plus the 240px sidebar and page padding exceeds 1280px. See e2e/layout/grid-baseline.json, and
 * §2.1 of the transformation plan for why 177 hard-coded column widths make this structural.
 *
 * tablet and phone are deliberately empty. The sidebar collapses below the desktop breakpoint, so
 * the arithmetic that produced the desktop list does not hold there, and a guessed baseline is
 * worse than none. Seed them from a real run: SEED_BASELINE=1.
 */
const KNOWN_OVERFLOW: Record<string, string[]> = {
  // Five shorter than it was. Moving the grid header inside its scroll container fixed
  // /customers, /catalog/products, /purchasing/suppliers, /reports/sales and
  // /reports/sales-analysis outright — the page had been sliding sideways because the header
  // spilled out of the panel, not because the columns did.
  //
  // CI is what said so: each of those reported "Expected to fail, but passed", which is the whole
  // point of listing them here rather than leaving the suite red. The five below still overrun and
  // will until their column widths are dealt with.
  desktop: [
    '/reports/stock-value',
    '/reports/on-order',
    '/reports/stock-position',
    '/reports/stock-received',
    '/admin/audit',
  ],
  tablet: [],
  phone: [],
};

async function signIn(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.waitForURL(/\/account\/login/);
  await page.getByLabel('Username or email').fill(CREDENTIALS.username);
  // exact, because the reveal toggle beside the field is labelled "Show password" and
  // getByLabel matches substrings — without this the locator resolves to two elements.
  await page.getByLabel('Password', { exact: true }).fill(CREDENTIALS.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await page.waitForURL((url) => !url.pathname.startsWith('/account'));
}

/**
 * How far the document overruns the window, and what is sticking out.
 *
 * The offending element is reported alongside the number because "the page is 312px too wide" is a
 * measurement, and "…because the grid header is 1950px" is a bug report.
 */
async function measureOverflow(page: Page) {
  return page.evaluate(() => {
    const doc = document.scrollingElement ?? document.documentElement;
    const overflow = doc.scrollWidth - doc.clientWidth;

    const culprits = [...document.querySelectorAll<HTMLElement>('*')]
      .filter((el) => el.getBoundingClientRect().right > doc.clientWidth + 1)
      .slice(0, 3)
      .map((el) => {
        const rect = el.getBoundingClientRect();
        const classes = typeof el.className === 'string' ? el.className.slice(0, 60) : '';
        return `${el.tagName.toLowerCase()}${classes ? `.${classes.trim().split(/\s+/).join('.')}` : ''} → ${Math.round(rect.right)}px`;
      });

    return { overflow, viewport: doc.clientWidth, culprits };
  });
}

async function settle(page: Page): Promise<void> {
  // The grids paint from a query, so a route can be briefly narrow before its columns arrive.
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await page.waitForTimeout(250);
}

test.describe('no route scrolls sideways', () => {
  test.describe('public routes', () => {
    for (const route of PUBLIC_ROUTES) {
      test(`${route} fits the window`, async ({ page }, testInfo) => {
        const known = KNOWN_OVERFLOW[testInfo.project.name] ?? [];
        if (known.includes(route) && !SEEDING) test.fail();

        await page.goto(route);
        await settle(page);

        const { overflow, viewport, culprits } = await measureOverflow(page);

        if (SEEDING) {
          if (overflow > 0) {
            console.log(`SEED ${testInfo.project.name} '${route}', // +${overflow}px — ${culprits.join('; ')}`);
          }
          return;
        }

        // <= 0, not === 0. A page whose content is narrower than the window reports a negative
        // difference and is perfectly correct; only a positive one is a sideways scrollbar.
        expect(
          overflow,
          `${route} overruns a ${viewport}px window by ${overflow}px. Widest: ${culprits.join('; ') || 'unknown'}`,
        ).toBeLessThanOrEqual(0);
      });
    }
  });

  test.describe('signed-in routes', () => {
    test.skip(!CREDENTIALS.password, 'Set E2E_PASSWORD to run the signed-in layout checks.');

    test.beforeEach(async ({ page }) => {
      await signIn(page);
    });

    for (const route of SIGNED_IN_ROUTES) {
      test(`${route} fits the window`, async ({ page }, testInfo) => {
        const known = KNOWN_OVERFLOW[testInfo.project.name] ?? [];
        if (known.includes(route) && !SEEDING) test.fail();

        await page.goto(route);
        await settle(page);

        const { overflow, viewport, culprits } = await measureOverflow(page);

        if (SEEDING) {
          if (overflow > 0) {
            console.log(`SEED ${testInfo.project.name} '${route}', // +${overflow}px — ${culprits.join('; ')}`);
          }
          return;
        }

        // <= 0, not === 0. A page whose content is narrower than the window reports a negative
        // difference and is perfectly correct; only a positive one is a sideways scrollbar.
        expect(
          overflow,
          `${route} overruns a ${viewport}px window by ${overflow}px. Widest: ${culprits.join('; ') || 'unknown'}`,
        ).toBeLessThanOrEqual(0);
      });
    }
  });
});
