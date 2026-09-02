import { existsSync, readFileSync } from 'node:fs';
import { defineConfig, devices } from '@playwright/test';

/**
 * Reads `.env.local` the way `next dev` does, so the suite runs with the same station, location and
 * credentials the app is already configured with.
 * <p>
 * Hand-rolled rather than pulled from `dotenv`: this needs `KEY=value` and nothing else, and the
 * alternative is a dependency in the test config for fifteen lines of parsing. A real environment
 * variable always wins, so CI can override anything here without editing a file.
 */
function loadEnvLocal(): void {
  if (!existsSync('.env.local')) return;

  for (const line of readFileSync('.env.local', 'utf8').split(/\r?\n/)) {
    const match = /^\s*([A-Z0-9_]+)\s*=\s*(.*)$/i.exec(line);
    if (!match) continue;

    const [, key, rawValue] = match;
    if (process.env[key] !== undefined) continue;

    // Quotes are a shell convention, not part of the value.
    process.env[key] = rawValue.trim().replace(/^(['"])(.*)\1$/, '$2');
  }
}

loadEnvLocal();

/**
 * End-to-end configuration.
 *
 * The suite exists for one class of assertion: properties that can only be checked in a real browser
 * against a real stack. The headline one is doc 07's "no token is reachable from JavaScript" — a
 * claim about what exists in a page, which no unit test can make.
 *
 * It needs the API and its dependencies running, so it is deliberately a separate command rather
 * than part of `npm test`.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  // Serial: these tests share one identity and one set of cookies, and parallel sign-ins would
  // interfere with each other's sessions.
  workers: 1,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',

  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  /**
   * Three widths, because one width proves nothing about a layout.
   *
   * Desktop Chrome is 1280×720, which is precisely the width at which the POS product picker
   * reappears — it is `display: none` below 1280 while its toggle stays live and does nothing. A
   * suite that only ever runs at 1280 cannot see that, and did not. The tablet and phone widths are
   * the two the brief names where the layout has to change rather than shrink.
   *
   * `isMobile` is left off deliberately: it switches Chromium into touch emulation, and these
   * projects exist to measure layout, not to re-test every interaction under a different input
   * model. Touch behaviour is worth its own project when something actually asserts on it.
   */
  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'] },
    },
    // The narrow projects run the layout specs only. The existing suite asserts on behaviour that
    // is width-independent — token storage, uploads, tender parsing — and re-running it at 390px
    // would triple its cost to re-prove the same facts, while any failure would be about a layout
    // those specs were never written against.
    {
      name: 'tablet',
      testMatch: /layout\/.*\.spec\.ts$/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 820, height: 1180 } },
    },
    {
      name: 'phone',
      testMatch: /layout\/.*\.spec\.ts$/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } },
    },
  ],

  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: 'npm run dev',
        url: 'http://localhost:3000',
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
      },
});
