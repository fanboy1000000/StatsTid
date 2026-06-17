import { defineConfig, devices } from '@playwright/test'

/**
 * SPRINT-82 (R1): real-browser E2E harness for the critical user journeys.
 *
 * Runs the journeys against the vite DEV server (port 3000) so the `/api`→:5100
 * proxy in vite.config.ts is in effect (vite preview has no proxy). The backend
 * 7-service docker-compose stack (:5100-5700) must be up — locally it already is;
 * in CI the dedicated `e2e-tests` job brings it up before invoking this suite.
 *
 * baseURL is parameterized via E2E_BASE_URL so the same journeys can later target
 * a deployed/staging environment (the 2026-05-18 deployment model) — NOT CI-only-shaped.
 */
export default defineConfig({
  testDir: 'e2e',
  // Per-test retries absorb transient flake (the real-clock stack flaked S77/S78;
  // FAIL-002 = Docker sheds containers under churn). Owner-ruled retries:2 (R3/OQ-2).
  retries: 2,
  // Fail the CI build if test.only is accidentally committed.
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['html', { open: 'never' }], ['list']] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    // Trace + video only on the first retry — cheap on green, diagnosable on flake.
    trace: 'on-first-retry',
    video: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // The harness owns the FE dev server: reuse the already-running :3000 dev server
  // locally; start a fresh one in CI (reuseExistingServer:false under CI).
  webServer: {
    command: 'npm run dev',
    port: 3000,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
