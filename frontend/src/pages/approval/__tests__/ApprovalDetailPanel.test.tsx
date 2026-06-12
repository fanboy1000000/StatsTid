// S72 Step-7a fix-forward (B1) — ApprovalDetailPanel through its ACTUAL wiring
// (the REAL useSkema + useBalanceSummary + SkemaGrid + BalanceSummary, fetch
// mocked at the network level): the leader review surface renders ALL rows
// (R12) from the UNION basis (catalogs ∪ served entry/absence keys) —
//   • a preference-HIDDEN project row IS visible (the legacy `projects` field
//     serves only the VISIBLE selection for configured users per the 7201
//     contract — the pre-fix panel built its rows from that field and could
//     not render hidden rows),
//   • a DEACTIVATED project absent from every catalog renders labeled by its
//     CODE (its historical hours are part of the record),
//   • the work-row ✓ state computes over ALL served allocations (R3).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ApprovalDetailPanel } from '../ApprovalDetailPanel'
import type { SkemaMonthData } from '../../../types'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

function buildDailyNorm(): { date: string; hours: number | null }[] {
  const out: { date: string; hours: number | null }[] = []
  for (let d = 1; d <= 31; d++) {
    const date = `2026-03-${String(d).padStart(2, '0')}`
    const dow = new Date(date + 'T00:00:00').getDay()
    out.push({ date, hours: dow === 0 || dow === 6 ? 0 : 7.4 })
  }
  return out
}

/** A CONFIGURED employee who selected ONLY Udvikling: per the 7201 contract the
    legacy `projects` field serves the VISIBLE selection only; Drift lives in
    `catalogs`; GAMMEL is a deactivated project that exists ONLY in `entries`. */
function makeMonthData(): SkemaMonthData {
  return {
    year: 2026,
    month: 3,
    daysInMonth: 31,
    projects: [
      { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', isActive: true, sortOrder: 1 },
    ],
    absenceTypes: [{ type: 'VACATION', label: 'Ferie' }],
    entries: [
      { date: '2026-03-02', projectCode: 'DRIFT', hours: 7.4 }, // preference-hidden
      { date: '2026-03-02', projectCode: 'UDV', hours: 2.1 },
      { date: '2026-03-05', projectCode: 'GAMMEL', hours: 4 }, // deactivated, catalog-less
    ],
    absences: [{ date: '2026-03-03', absenceType: 'VACATION', hours: 7.4, feriedage: 1 }],
    approval: null,
    workTime: [
      // Mar 2 worked 9,5 = allocated 7,4 (hidden Drift) + 2,1 (visible Udvikling)
      { date: '2026-03-02', intervals: [{ start: '08:00', end: '17:30' }], manualHours: 0 },
    ],
    dailyNorm: buildDailyNorm(),
    rowPreferences: {
      configured: true,
      projects: [{ projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 0 }],
      absenceTypes: [{ type: 'VACATION', label: 'Ferie', sortOrder: 0 }],
    },
    catalogs: {
      projects: [
        { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', isActive: true, sortOrder: 0 },
        { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', isActive: true, sortOrder: 1 },
      ],
      absenceTypes: [{ type: 'VACATION', label: 'Ferie' }],
    },
    boundaryWorkTime: [],
    fullDayNormAtMonthEnd: 7.4,
  }
}

const summaryData = {
  flexBalance: 0,
  flexDelta: 0,
  vacationDaysUsed: 0,
  vacationDaysEntitlement: 25,
  normHoursExpected: 0,
  normHoursActual: 0,
  overtimeHours: 0,
  agreementCode: 'AC',
  hasMerarbejde: false,
  entitlements: [],
}

beforeEach(() => {
  mockFetch.mockReset()
  mockFetch.mockImplementation(async (url: string) => {
    if (url.includes('/api/skema/emp001/month')) return jsonResponse(makeMonthData())
    if (url.includes('/api/balance/emp001/summary')) return jsonResponse(summaryData)
    return jsonResponse({})
  })
})

function gridRow(container: HTMLElement, label: string): HTMLElement {
  const rows = Array.from(container.querySelectorAll('tbody tr'))
  const row = rows.find((r) => r.querySelector('td')?.textContent?.startsWith(label))
  if (!row) throw new Error(`grid row "${label}" not found`)
  return row as HTMLElement
}

function renderPanel() {
  return render(
    <ApprovalDetailPanel
      period={{
        periodId: 'per-1',
        employeeId: 'emp001',
        periodStart: '2026-03-01',
        periodEnd: '2026-03-31',
      }}
    />,
  )
}

describe('ApprovalDetailPanel — R12/B1 full-record rows (actual wiring)', () => {
  it('B1: a preference-HIDDEN project row IS visible on the review surface (the legacy projects field is the visible selection — the rows come from the union basis)', async () => {
    const { container } = renderPanel()
    // Drift is NOT in `projects` (the employee hid it) — the leader still sees it
    expect(await screen.findByText('Drift & support')).toBeInTheDocument()
    expect(screen.getByText('Udvikling')).toBeInTheDocument()
    // …and its hours render in the row (Mar 2, 7,4)
    const driftCells = gridRow(container, 'Drift & support').querySelectorAll('td')
    expect(driftCells[2].textContent).toBe('7,4')
  })

  it('B1: a DEACTIVATED project absent from the catalogs renders by its CODE with its historical hours', async () => {
    const { container } = renderPanel()
    await screen.findByText('Drift & support')
    const gammelRow = gridRow(container, 'GAMMEL')
    const cells = gammelRow.querySelectorAll('td')
    expect(cells[5].textContent).toBe('4') // Mar 5 — formatCell trims ",0"
  })

  it('R12/R3: read-only — no inputs — and the work-row ✓ computes over ALL served allocations (incl. the hidden Drift hours)', async () => {
    const { container } = renderPanel()
    await screen.findByText('Drift & support')
    expect(container.querySelectorAll('input')).toHaveLength(0)
    // worked 9,5 = 7,4 (hidden) + 2,1 (visible) → balanced ✓ — the pre-fix
    // visible-selection basis saw only 2,1 allocated and showed amber 7,4
    const workRow = gridRow(container, 'Arbejdstid')
    expect(workRow.querySelectorAll('td')[2].textContent).toBe('✓')
  })
})
