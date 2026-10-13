import { defineConfig, devices } from '@playwright/test'

// Dedicated ports so E2E runs never collide with a local `dotnet run` (5195) or `npm run dev` (5173).
const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5295'
const webUrl = process.env.E2E_WEB_URL ?? 'http://127.0.0.1:5273'
const isCi = !!process.env.CI

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  // The API, Vite and the browsers share one machine; more workers than this slows every test down locally.
  workers: isCi ? 2 : 4,
  // Most tests sign in through the UI, often as several users.
  timeout: 60_000,
  reporter: isCi ? [['list'], ['html', { open: 'never' }]] : 'list',
  // The Vite dev server compiles pages on first request, which is slow while many browsers run in parallel.
  expect: { timeout: 10_000 },
  use: {
    baseURL: webUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // Requires `docker compose up -d` (Postgres). Recreates the hrportal_e2e database each run.
      name: 'api',
      command: 'node scripts/start-api.mjs',
      url: `${apiUrl}/health`,
      env: { E2E_API_URL: apiUrl },
      timeout: 180_000,
      reuseExistingServer: false,
      stdout: 'pipe',
    },
    {
      name: 'web',
      command: `npm --prefix ../web run dev -- --host 127.0.0.1 --port ${new URL(webUrl).port}`,
      url: webUrl,
      env: { HRPORTAL_API_URL: apiUrl, VITE_AUTH_MODE: 'dev' },
      timeout: 120_000,
      reuseExistingServer: !isCi,
    },
  ],
})
