import { test, expect } from '@playwright/test'
import { login } from './helpers/auth'

/**
 * S125 / TASK-12504 (F3) — every lazy route resolves, renders, and never blanks the shell.
 *
 * <b>Why this exists as an E2E rather than a unit test.</b> F3 converted 16 page imports to
 * `React.lazy(() => import(...).then(m => ({ default: m.Name })))`. The vitest suite imports those
 * pages DIRECTLY, so it structurally cannot catch a broken lazy mapping: a wrong named-export
 * binding compiles, type-checks, and passes all 711 unit tests while rendering a blank content area
 * in the browser. Only driving the real router exercises the mapping.
 *
 * It also pins the placement of the Suspense boundary. It sits INSIDE `AppLayout`, around the
 * `<Outlet />`, so the header/nav/sidebar stay on screen while a chunk loads and only the content
 * region swaps. Asserting the header stays visible across every navigation is what stops someone
 * later "simplifying" the boundary up to the router, which would blank the whole window on each
 * route change.
 *
 * Assertions AUTO-RETRY rather than reading `innerText` once. The first draft of this check did the
 * latter and reported "content EMPTY" for pages that were merely still fetching — a probe bug that
 * looks exactly like the defect it is meant to find. If this test ever reports an empty content
 * area, check the timeout before believing it.
 */
const ROUTES = [
  '/tid/registrering',
  '/tid/oversigt',
  '/tid/mine-perioder',
  '/admin/organisation-medarbejdere',
  '/admin/ledelseslinjer',
  '/admin/auditlog',
  '/admin/projekter',
  '/admin/brugerrettigheder',
  '/global/overenskomster',
  '/global/organisation',
  '/global/loenartstilknytning',
  '/global/entitlement-configs',
  '/health',
]

test('every lazy route resolves, renders, and never blanks the shell', async ({ page }) => {
  const chunkErrors: string[] = []
  page.on('console', m => {
    if (m.type() === 'error' && /dynamically imported module|Loading chunk|is not a function/i.test(m.text()))
      chunkErrors.push(m.text())
  })
  page.on('pageerror', e => chunkErrors.push(`pageerror: ${e.message}`))

  await login(page, 'demo_admin')

  const failures: string[] = []
  for (const path of ROUTES) {
    await page.goto(path)
    const main = page.locator('main').first()
    try {
      // The shell must survive the chunk load — this is the Suspense-placement pin.
      await expect(page.locator('header').first()).toBeVisible({ timeout: 10000 })
      await expect(main).not.toBeEmpty({ timeout: 15000 })
      const text = (await main.innerText()).trim()
      if (text.length < 15) failures.push(`${path}: content area still ~empty ("${text}")`)
    } catch (e) {
      failures.push(`${path}: ${(e as Error).message.split('\n')[0]}`)
    }
  }

  console.log(`LAZY ROUTES: ${ROUTES.length} checked | failures: ${failures.length} | chunk errors: ${chunkErrors.length}`)
  failures.forEach(f => console.log('  FAIL ' + f))
  chunkErrors.forEach(e => console.log('  CHUNK-ERR ' + e))

  expect(chunkErrors, 'no dynamic-import failures').toEqual([])
  expect(failures, 'every lazy route rendered').toEqual([])
})
