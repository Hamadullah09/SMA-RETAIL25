import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

/**
 * Unit tests — the ones that need neither a browser nor the API.
 *
 * Deliberately separate from Playwright. The end-to-end suite proves things only a real browser can
 * against a real stack, so it needs the whole system up; these are pure functions and must be
 * runnable in the frontend CI job, which has no database, no API and no server. Keeping them apart
 * is what lets a rule about class merging be checked on every push rather than only when the stack
 * happens to be healthy.
 */
export default defineConfig({
  test: {
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    environment: 'node',
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
});
