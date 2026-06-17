/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      '/api': 'http://localhost:5100'
    }
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
    // Vitest owns the component tier (src/**); the Playwright E2E specs live in
    // e2e/** and import @playwright/test (incompatible with the vitest runner).
    // Scope the runner to src so the two tiers never collide (SPRINT-82 R7).
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    // S82/8204: the suite grew (the new userEvent-heavy approval-a11y + MyPeriods
    // tests push the full run to ~200s); under vitest's default parallel pool the
    // heaviest userEvent tests (SkemaPage "page chrome", the ApprovalDashboard
    // reject cross-flows) stretch past the default 5s per-test ceiling under load
    // and time out — even though each passes in <1s in isolation. This is the
    // documented FAIL-002/SkemaPage load-contention flake class, not a code defect.
    // CI's `frontend-build` runs `vitest run` with NO retries, so a tight ceiling
    // reds the gated job. Raise the ceiling (it only bounds genuinely-long tests;
    // no penalty for the fast majority) so the suite is reliably green under load.
    testTimeout: 30000,
    hookTimeout: 30000,
  }
})
