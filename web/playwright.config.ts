import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests against the built static site (`npm run build` first), served by `e2e/serve.mjs` the way the
 * server serves it. The API is answered in the browser from contract fixtures (`e2e/mock-api.ts`), so no server or
 * database is needed.
 *
 * - `desktop` (1366×768, the shops' own screens) runs every spec;
 * - `phone` (390×844) runs the layout spec;
 * - `screens` saves screenshots of the main pages at both sizes to SCREENS_DIR (only with `npm run screens`).
 */
const port = Number(process.env.E2E_PORT ?? 4173);

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    locale: 'en-US',
    timezoneId: 'America/Toronto',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'desktop',
      testIgnore: /screens\.spec\.ts$/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 } },
    },
    {
      name: 'phone',
      testMatch: /layout\.spec\.ts$/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 }, hasTouch: true },
    },
    {
      name: 'screens',
      testMatch: /screens\.spec\.ts$/,
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: `node e2e/serve.mjs ${port}`,
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: !process.env.CI,
    timeout: 30_000,
  },
});
