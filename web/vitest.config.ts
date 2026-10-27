import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

/** Unit tests: plain functions, no browser and no server. Playwright (`npm run e2e`) covers the pages. */
export default defineConfig({
  test: {
    include: ['src/**/*.test.ts'],
    environment: 'node',
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
});
