import { defineConfig } from '@playwright/test';

/**
 * End-to-end tests (#14) run against the Docker Compose stack (`docker compose up --build` from the
 * repository root): the real web build on :4200, the API on :5208 and a migrated SQL Server.
 * Locally: `npm run e2e`. CI starts the stack itself (.github/workflows/e2e.yml).
 */
export default defineConfig({
  testDir: 'e2e',
  // The stack is shared and the specs are few; one at a time keeps failures easy to read.
  workers: 1,
  fullyParallel: false,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    // Locally, E2E_CHANNEL=chrome uses the installed Chrome, so no browser download is needed.
    channel: process.env['E2E_CHANNEL'] || undefined,
    trace: 'retain-on-failure',
  },
});
