// S143 / TASK-14303 — ManagerSkemaGrid is assigned to 14303 "by name," not because it reads the
// clock itself (it does not — grep for `new Date()` in `ManagerSkemaGrid.tsx` finds nothing), but
// because the Teamoversigt detail panel renders the SAME `SkemaGrid` this task migrated off
// `new Date()` onto the shared calendar context (`useCalendarToday()`,
// `contexts/CalendarContext.tsx`). Assigning the component AND this proof to one task — rather
// than splitting "fix SkemaGrid" and "prove it reaches Teamoversigt" across 14303/14304 — is what
// SPRINT-143.md calls avoiding the "ordered is not atomic" split: two tasks that each land clean
// but whose combination silently doesn't work.
//
// SCOPE: this test renders `ManagerSkemaGrid` DIRECTLY, wrapped only in `renderWithCalendar` (the
// shared test seam TASK-14301 shipped) — never through `TeamOversigt` (TASK-14304's file, out of
// this task's authorized scope). It proves the WIRING (the context value really does reach the
// nested grid and really does drive the highlight), independent of whatever `TeamOversigt.tsx`
// does around it. `useCalendarToday`'s own throw-outside-provider contract is already pinned at
// the context level (`contexts/__tests__/CalendarContext.test.tsx`) — not repeated here.
import { describe, it, expect, vi } from 'vitest'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import { ManagerSkemaGrid } from '../ManagerSkemaGrid'
import type { SkemaMonthData } from '../../../types'

// A minimal but SPEC-SHAPED month fixture (every `SkemaMonthResponse` member present) — empty
// rows are irrelevant to what this test proves (the header's "today" highlight, which renders
// independently of `rows`), so no project/absence data is needed.
const stable = vi.hoisted(() => {
  const monthData: SkemaMonthData = {
    year: 2026,
    month: 3,
    daysInMonth: 31,
    projects: [],
    absenceTypes: [],
    entries: [],
    absences: [],
    workTime: [],
    dailyNorm: [],
    approval: null,
    employeeDeadline: '2026-04-05',
    managerDeadline: '2026-04-10',
    rowPreferences: { configured: false, projects: [], absenceTypes: [] },
    catalogs: { projects: [], absenceTypes: [] },
    boundaryWorkTime: [],
    fullDayNormAtMonthEnd: null,
    consumptionBasis: [],
  }
  return { data: monthData, loading: false, error: null }
})

// Keep the real `deriveSkemaRowBasis` export callable (ManagerSkemaGrid imports it alongside the
// hook) — stub ONLY `useSkema`, mirroring `SkemaPageParamInit.test.tsx`'s established pattern.
vi.mock('../../../hooks/useSkema', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../hooks/useSkema')>()),
  useSkema: () => stable,
}))

describe('ManagerSkemaGrid — the nested SkemaGrid highlights the calendar context\'s "today" (S143)', () => {
  it('renders inline with no calendar-context wrapper of its own, yet the nested grid still highlights the INJECTED "today"', () => {
    const { container } = renderWithCalendar(
      '2026-03-11',
      <ManagerSkemaGrid employeeId="emp001" year={2026} month={3} />,
    )
    // headers[0] = 'Dato' label, headers[d] = day d (March 2026 has 31 days).
    const headers = container.querySelectorAll('thead th')
    expect(headers[11].className).toContain('today')
    expect(headers[12].className).not.toContain('today')
  })

  it('a DIFFERENT injected "today" highlights a DIFFERENT day — not a hardcoded one', () => {
    const { container } = renderWithCalendar(
      '2026-03-20',
      <ManagerSkemaGrid employeeId="emp001" year={2026} month={3} />,
    )
    const headers = container.querySelectorAll('thead th')
    expect(headers[20].className).toContain('today')
    expect(headers[11].className).not.toContain('today')
  })
})
