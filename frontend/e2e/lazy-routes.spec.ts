import { readFileSync } from 'node:fs'
import { join } from 'node:path'
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
 * Assertions AUTO-RETRY rather than reading `innerText` once, and assert only what is MEANINGFUL:
 * no dynamic-import error, the shell still present, and the content region non-empty. Two earlier
 * drafts each added a crude "is there enough text?" heuristic and each produced FALSE failures —
 * first on pages still fetching, then on pages that legitimately render little text (the delegation
 * page's body is the single word "Vikariering"). A probe that invents its own success criteria
 * manufactures defects that look exactly like the real one. Assert the mechanism, not the prose.
 */
/**
 * Every lazy page in App.tsx must appear here. The S125 close review found six missing
 * (TeamOversigt, DelegationPage, ConfigManagement, PositionOverrideManagement,
 * AgreementConfigEditor, NotFoundPage) — a route-coverage gap in the very test written to prove
 * the mappings work. The `lazy-page coverage` test below now FAILS if App.tsx gains a lazy page
 * that nothing here exercises, so the gap cannot silently reopen.
 */
const ROUTES: Array<{ path: string; page: string }> = [
  { path: '/tid/registrering', page: 'SkemaPage' },
  { path: '/tid/oversigt', page: 'ArsoversigtPage' },
  { path: '/tid/mine-perioder', page: 'MyPeriods' },
  { path: '/godkend/oversigt', page: 'TeamOversigt' },
  { path: '/godkend/vikariering', page: 'DelegationPage' },
  { path: '/admin/organisation-medarbejdere', page: 'OrganisationOgMedarbejdere' },
  { path: '/admin/ledelseslinjer', page: 'RoleManagement' },
  { path: '/admin/auditlog', page: 'AuditLogView' },
  { path: '/admin/projekter', page: 'ProjectManagement' },
  { path: '/admin/brugerrettigheder', page: 'RoleManagement' },
  { path: '/lokal/ok-konfiguration', page: 'ConfigManagement' },
  { path: '/lokal/stillingstilpasninger', page: 'PositionOverrideManagement' },
  { path: '/global/overenskomster', page: 'AgreementConfigList' },
  { path: '/global/overenskomster/new', page: 'AgreementConfigEditor' },
  { path: '/global/organisation', page: 'OrganisationOgMedarbejdere' },
  { path: '/global/loenartstilknytning', page: 'WageTypeMappingManagement' },
  { path: '/global/entitlement-configs', page: 'ConfigManagement' },
  { path: '/health', page: 'HealthDashboard' },
  { path: '/no-such-route-xyz', page: 'NotFoundPage' },
]

/**
 * A ROUTE-COVERAGE guard, not a browser test. The close review found six lazy pages absent from the
 * route list above — the test meant to prove the mappings work was silently not exercising a third
 * of them. Reading App.tsx and comparing is what stops that recurring the next time a page is added.
 */
test('every lazy page in App.tsx is exercised by a route above', () => {
  const app = readFileSync(join(__dirname, '..', 'src', 'App.tsx'), 'utf8')
  const lazyPages = [...app.matchAll(/^const (\w+) = lazy\(/gm)].map(m => m[1])
  const covered = new Set(ROUTES.map(r => r.page))
  const uncovered = lazyPages.filter(p => !covered.has(p))
  expect(uncovered, 'lazy pages with no route in this spec').toEqual([])
})

test('every lazy route resolves, renders, and never blanks the shell', async ({ page }) => {
  const chunkErrors: string[] = []
  page.on('console', m => {
    if (m.type() === 'error' && /dynamically imported module|Loading chunk|is not a function/i.test(m.text()))
      chunkErrors.push(m.text())
  })
  page.on('pageerror', e => chunkErrors.push(`pageerror: ${e.message}`))

  await login(page, 'demo_admin')

  const failures: string[] = []
  for (const { path } of ROUTES) {
    await page.goto(path)
    const main = page.locator('main').first()
    try {
      // The shell must survive the chunk load — this is the Suspense-placement pin.
      await expect(page.locator('header').first()).toBeVisible({ timeout: 10000 })
      // The route MOUNTED: the Suspense fallback is an empty placeholder, so a resolved page is
      // exactly the difference between empty and not. A broken lazy mapping fails here AND raises a
      // pageerror — verified by deliberately mis-mapping a page.
      await expect(main).not.toBeEmpty({ timeout: 15000 })
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
