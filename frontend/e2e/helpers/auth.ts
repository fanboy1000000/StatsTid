import { expect, type Page } from '@playwright/test'

/**
 * SPRINT-82 task 8202 — the shared login helper, lifted verbatim from the
 * established journey-1 pattern (e2e/login.spec.ts): the real JWT auth round-trip
 * through the vite `/api`→:5100 proxy. Fresh login per spec (isolation-first; no
 * shared storageState — a recorded perf follow-on).
 *
 * Robust selectors only: the login form uses real <label htmlFor> associations
 * (FormField → Label); the regex match tolerates the required-marker " *" suffix.
 */
export async function login(page: Page, username: string, password = 'password'): Promise<void> {
  await page.goto('/login')
  await page.getByLabel(/Brugernavn/).fill(username)
  await page.getByLabel(/Adgangskode/).fill(password)
  await page.getByRole('button', { name: 'Log ind' }).click()

  // Successful auth → index redirect to /tid/registrering; the authenticated app
  // shell renders the "Log ud" action (absent on the unauthenticated screen).
  await expect(page).toHaveURL(/\/tid\/registrering$/)
  await expect(page.getByRole('button', { name: 'Log ud' })).toBeVisible()
}
