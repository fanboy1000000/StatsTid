// S143 / TASK-14310 — Proof 2: the post-midnight PAGE mount.
//
// PLAIN-LANGUAGE WHY. `contexts/CalendarContext.tsx` and `hooks/useCalendarBootstrap.ts`
// (TASK-14301) already prove the PROVIDER side of the Copenhagen day rollover: the shared "today"
// refreshes at midnight, and — separately — an ALREADY-MOUNTED consumer's month must NOT jump when
// that refresh lands mid-session (`CalendarContextValue.today`'s own "snapshot once" documentation).
// Neither of those provider-level suites, nor `SkemaPage`'s own drill-in tests
// (`SkemaPageParamInit.test.tsx`) or its AC-5 device-vs-server test (`SkemaPage.test.tsx`), cover
// the remaining case this file exists for: a PAGE that has never been mounted before, mounting for
// the FIRST time AFTER the day has already rolled over to a new month. Concretely — an admin's
// calendar day was last confirmed as 31 January; they close the tab (or never opened this screen);
// the Copenhagen rollover refresh completes overnight; and only THEN, for the first time this
// session, do they open `SkemaPage`. It must open on February — never seed "yesterday's" month,
// January, which is the exact defect this whole migration (TASK-14201 onward) exists to remove.
//
// WHY THIS EARNS ITS OWN TEST RATHER THAN DUPLICATING "defaults to today" COVERAGE. Every existing
// test that renders `SkemaPage` mounts the `CalendarContext.Provider` and `SkemaPage` TOGETHER, in
// ONE render call, with a SINGLE fixed `today` value that never changes for that render's lifetime
// (`SkemaPageParamInit.test.tsx`'s "defaults to today when no params are present"; `SkemaPage.test.
// tsx`'s AC-5). That proves the page reads context AT ALL — it does not prove the page carries no
// hidden memory of a PREVIOUS mount's value into a fresh one (a module-scope cache, or any other
// piece of state living OUTSIDE the component instance and therefore outliving an unmount).
// `SkemaPage.tsx`'s actual `useState(todayYear...)`/`useState(todayMonth...)` initializers, read
// directly from `useCalendarToday()` on the component's own first render, make that class of bug
// implausible today — but nothing in the existing suite would catch a REGRESSION to cross-mount
// caching if one were introduced later. This test closes that gap: mount once with a January
// "today", unmount it (the closest a component test gets to "the tab was closed"), then mount FRESH
// with a February "today" — the second, brand-new mount must show February.
//
// THE WRONG IMPLEMENTATION THIS CATCHES: any `SkemaPage` that seeds its year/month from something
// other than the CURRENT render's `useCalendarToday()` value read fresh at mount time — most
// concretely, a helper or module-scope variable that caches "the first calendar day this module
// ever saw" and reuses it on every later mount regardless of what the context now reports. Such a
// bug would pass every OTHER test in this suite (none of them mount the page twice with different
// values) and would surface exactly as "seeds yesterday's month" on the second mount below.
//
// VERIFIED BY MUTATION, not merely reasoned. A scratch copy of `SkemaPage.tsx`'s year/month
// initializers was changed to read through a module-scope variable set on first read and never
// updated afterwards (the caching bug described above); with that mutation in place, this test
// failed exactly as predicted (the second mount's heading still read "Januar 2026" instead of
// "Februar 2026"), and passed again once the mutation was reverted. The committed `SkemaPage.tsx`
// is untouched by this file — the mutation lived only in the scratch check.
//
// RENDER SETUP: identical stubbing to `SkemaPageParamInit.test.tsx` — the page's month heading is
// the only thing under test, so the grid/panel/manager children are stubbed to `null` and the data
// hooks return a stable idle shape (referentially stable across renders — SkemaPage has effects
// keyed on `data` identity) so no network or debounce machinery needs to run.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { renderWithCalendar } from '../../test/renderWithCalendar'

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({
    user: { employeeId: 'emp001', role: 'Employee' },
    role: 'Employee',
    orgId: 'STY01',
    agreementCode: 'AC',
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  }),
}))

// Stable idle data hooks — SkemaPage renders its month heading regardless. CRITICAL: the returned
// objects MUST be referentially STABLE across renders (mirrors SkemaPageParamInit.test.tsx).
const stable = vi.hoisted(() => {
  const skemaData = {
    entries: [],
    absences: [],
    projects: [],
    absenceTypes: [],
    workTime: [],
    dailyNorm: [],
    approval: null,
  }
  return {
    skema: {
      data: skemaData,
      loading: false,
      error: null,
      quotaError: null,
      approvalValidationError: null,
      clearQuotaError: () => {},
      clearApprovalValidationError: () => {},
      refetch: () => {},
      saveMonth: () => {},
      employeeApprove: () => {},
      submitAndApprove: () => {},
      reopenPeriod: () => {},
    },
    balance: { data: null, loading: false, error: null, refetch: () => {} },
    compliance: { result: null, loading: false, error: null, refetch: () => {} },
  }
})

vi.mock('../../hooks/useSkema', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useSkema')>()),
  useSkema: () => stable.skema,
}))
vi.mock('../../hooks/useBalanceSummary', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../hooks/useBalanceSummary')>()),
  useBalanceSummary: () => stable.balance,
}))
vi.mock('../../hooks/useCompliance', () => ({ useCompliance: () => stable.compliance }))

vi.mock('../../components/SkemaGrid', () => ({ SkemaGrid: () => null }))
vi.mock('../../components/BalanceSummary', () => ({ BalanceSummary: () => null }))
vi.mock('../../components/ComplianceWarnings', () => ({ ComplianceWarnings: () => null }))
vi.mock('../../components/SkemaDayPanel', () => ({ SkemaDayPanel: () => null }))
vi.mock('../../components/SkemaProjectManager', () => ({ SkemaProjectManager: () => null }))

import { SkemaPage } from '../SkemaPage'

// No `?year=&month=` param — every mount below relies purely on the calendar seam's default.
function renderFresh(today: string) {
  return renderWithCalendar(
    today,
    <MemoryRouter initialEntries={['/tid/registrering']}>
      <SkemaPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.useRealTimers()
})

describe('SkemaPage — a first mount that lands after the day has already rolled over', () => {
  it('opens on the NEW month, not the one confirmed before midnight', () => {
    // "A day confirmed before midnight": an earlier mount (an earlier tab, an earlier session)
    // saw the server's day as still 31 January.
    const beforeMidnight = renderFresh('2026-01-31')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Januar 2026')
    beforeMidnight.unmount()

    // "The refresh already completed, and then SkemaPage mounted after midnight": this is a
    // BRAND-NEW mount, not a re-render of the one above — exactly like opening the page for the
    // first time in a new session, after the Copenhagen rollover has already landed.
    renderFresh('2026-02-01')
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Februar 2026')
    expect(screen.getByRole('heading', { level: 2 })).not.toHaveTextContent('Januar 2026')
  })
})
