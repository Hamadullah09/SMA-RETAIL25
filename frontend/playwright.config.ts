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

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
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
