import { test, expect } from '@playwright/test'

/**
 * SPRINT-82 journey 1 (R2a): login → dashboard.
 *
 * A seeded dev user (emp001 / password) performs the real JWT auth round-trip
 * (POST /api/auth/login through the vite proxy → auth service :5100), then the
 * app lands on the index redirect (/tid/registrering) inside the authenticated
 * app shell. Fresh login — no shared storageState (isolation-first; a shared
 * storageState is a recorded perf follow-on).
 */
test('emp001 logs in and lands on the registration dashboard', async ({ page }) => {
  await page.goto('/login')

  // Robust selectors: the login form uses real <label htmlFor> associations
  // (FormField → Label). Regex match tolerates the required-marker " *" suffix.
  await page.getByLabel(/Brugernavn/).fill('emp001')
  await page.getByLabel(/Adgangskode/).fill('password')
  await page.getByRole('button', { name: 'Log ind' }).click()

  // Successful auth → index redirect to /tid/registrering.
  await expect(page).toHaveURL(/\/tid\/registrering$/)

  // Authenticated app shell is visible: the Header renders the logged-in user id
  // and the "Log ud" action (both absent on the unauthenticated /login screen).
  await expect(page.getByRole('button', { name: 'Log ud' })).toBeVisible()
  await expect(page.getByText('emp001')).toBeVisible()
})
