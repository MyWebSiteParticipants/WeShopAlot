import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  use: {
    baseURL: 'https://localhost:7244',
    ignoreHTTPSErrors: true,          // the local dev certificate
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',       // a step-by-step replay when a test fails
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});