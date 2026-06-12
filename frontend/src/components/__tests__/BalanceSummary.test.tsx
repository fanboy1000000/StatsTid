// S72 / TASK-7205 — component-level tests for the 4-card balance strip
// (design_handoff_skema README §2; SPRINT-72 R10 + owner ruling D-A). The
// page-level integration pins (R2 grid reconciliation over live state, the
// null-feriedage skip end-to-end) live in pages/__tests__/SkemaPage.test.tsx;
// this file pins the COMPONENT contract plus the pure useBalanceSummary
// derivation helpers it consumes.
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { BalanceSummary } from '../BalanceSummary'
import {
  computeMonthFlexDelta,
  deriveMonthAbsenceUsage,
  type BalanceSummary as BalanceSummaryData,
  type MonthAbsenceUsage,
} from '../../hooks/useBalanceSummary'

const summaryData: BalanceSummaryData = {
  flexBalance: 4.2,
  // Deliberately DIFFERENT from any month aggregate: /summary's flexDelta is the
  // LAST event's delta (R10/Reviewer W3) — the component must NEVER render it on
  // the "Denne måned" sub-line.
  flexDelta: 99.9,
  vacationDaysUsed: 5,
  vacationDaysEntitlement: 25,
  normHoursExpected: 162.8,
  normHoursActual: 155.0,
  overtimeHours: 2.0,
  agreementCode: 'AC',
  hasMerarbejde: true,
  entitlements: [
    { type: 'VACATION', label: 'Ferie', totalQuota: 25, used: 8, planned: 0, carryoverIn: 3, remaining: 17, earned: 20.8, entitlementYear: 2025 },
    { type: 'SPECIAL_HOLIDAY', label: 'Særlige feriedage', totalQuota: 5, used: 1, planned: 0, carryoverIn: 0, remaining: 4, earned: 5, entitlementYear: 2025 },
    { type: 'CARE_DAY', label: 'Omsorgsdage', totalQuota: 2, used: 0, planned: 0, carryoverIn: 0, remaining: 2, earned: 2, entitlementYear: 2026 },
    // An age-eligible extra entitlement — must NOT become a 5th card (R10:
    // the dynamic additional-entitlement cards are removed from Skema).
    { type: 'SENIOR_DAY', label: 'Seniordage', totalQuota: 2, used: 0, planned: 0, carryoverIn: 0, remaining: 2, earned: 2, entitlementYear: 2026 },
  ],
}

const usage = (entries: [string, MonthAbsenceUsage][]) => new Map(entries)

type StripProps = Parameters<typeof BalanceSummary>[0]

function renderStrip(overrides: Partial<StripProps> = {}) {
  const props: StripProps = {
    data: summaryData,
    loading: false,
    month: 3,
    monthFlexDelta: -0.1,
    fullDayNormAtMonthEnd: 7.4,
    monthAbsenceUsage: usage([['VACATION', { hours: 14.8, days: 2 }]]),
    ...overrides,
  }
  return render(<BalanceSummary {...props} />)
}

describe('BalanceSummary — the fixed 4-card strip (R10)', () => {
  it('renders EXACTLY the 4 handoff cards: Flex saldo, Ferie, Særlige feriedage, Omsorgsdage', () => {
    const { container } = renderStrip()
    expect(screen.getByText('Flex saldo')).toBeInTheDocument()
    expect(screen.getByText('Ferie')).toBeInTheDocument()
    expect(screen.getByText('Særlige feriedage')).toBeInTheDocument()
    expect(screen.getByText('Omsorgsdage')).toBeInTheDocument()
    expect(container.querySelectorAll('[class*="card"]').length).toBe(4)
  })

  it('R10 information relocation: the old Normtimer / Merarbejde / additional-entitlement cards are GONE', () => {
    renderStrip()
    expect(screen.queryByText('Normtimer')).toBeNull()
    expect(screen.queryByText('Merarbejde')).toBeNull()
    expect(screen.queryByText('Overarbejde')).toBeNull()
    // SENIOR_DAY entitlement served but renders no card
    expect(screen.queryByText('Seniordage')).toBeNull()
  })

  it('renders nothing when no /summary data and not loading; 4 skeletons while loading', () => {
    const { container } = renderStrip({ data: null })
    expect(container.innerHTML).toBe('')
    const { container: loadingContainer } = renderStrip({ data: null, loading: true })
    expect(loadingContainer.querySelectorAll('[class*="skeleton"]').length).toBeGreaterThanOrEqual(4)
  })
})

describe('BalanceSummary — Flex card (R10 HYBRID sourcing)', () => {
  it('headline comes from /summary flexBalance, green when ≥ 0', () => {
    renderStrip()
    const num = screen.getByText('+4,2')
    expect(num.className).toContain('pos')
  })

  it('headline turns red when the /summary saldo is negative', () => {
    renderStrip({ data: { ...summaryData, flexBalance: -1.5 } })
    const num = screen.getByText('-1,5')
    expect(num.className).toContain('neg')
  })

  it('"Denne måned" renders the MONTH-GET-DERIVED prop — never /summary.flexDelta (the last-event delta)', () => {
    renderStrip({ monthFlexDelta: -0.1 })
    const flexCard = screen.getByText('Flex saldo').closest('div') as HTMLElement
    expect(flexCard.textContent).toContain('Denne måned -0,1 t')
    // /summary.flexDelta is 99.9 — must appear NOWHERE
    expect(flexCard.textContent).not.toContain('99,9')
  })

  it('"Norm" sub-line renders the served fullDayNormAtMonthEnd', () => {
    renderStrip()
    const flexCard = screen.getByText('Flex saldo').closest('div') as HTMLElement
    expect(flexCard.textContent).toContain('Norm 7,4 t')
  })

  it('"Denne måned" em-dashes when month data is unavailable (null prop)', () => {
    renderStrip({ monthFlexDelta: null })
    const flexCard = screen.getByText('Flex saldo').closest('div') as HTMLElement
    expect(flexCard.textContent).toContain('Denne måned —')
  })
})

describe('BalanceSummary — D-A hours-first day cards', () => {
  it('D-A: headline = days_available × fullDayNormAtMonthEnd "t tilbage", sub = "<days> dage"', () => {
    renderStrip()
    const ferieCard = screen.getByText('Ferie').closest('div') as HTMLElement
    // 17 × 7,4 = 125,8
    expect(ferieCard.textContent).toContain('125,8')
    expect(ferieCard.textContent).toContain('t tilbage')
    expect(ferieCard.textContent).toContain('17 dage')
    const saerligCard = screen.getByText('Særlige feriedage').closest('div') as HTMLElement
    expect(saerligCard.textContent).toContain('29,6') // 4 × 7,4
    expect(saerligCard.textContent).toContain('4 dage')
    const omsorgCard = screen.getByText('Omsorgsdage').closest('div') as HTMLElement
    expect(omsorgCard.textContent).toContain('14,8') // 2 × 7,4
    expect(omsorgCard.textContent).toContain('2 dage')
  })

  it('D-A fail-soft pin: a NULL norm scalar em-dashes the hours headline while the DAYS value still shows', () => {
    renderStrip({ fullDayNormAtMonthEnd: null })
    const ferieCard = screen.getByText('Ferie').closest('div') as HTMLElement
    const headline = ferieCard.querySelector('[class*="num"]') as HTMLElement
    expect(headline.textContent).toBe('—') // no client-side norm math, ever
    expect(ferieCard.textContent).toContain('t tilbage')
    expect(ferieCard.textContent).toContain('17 dage') // the day-based record stays visible
    expect(ferieCard.textContent).not.toContain('125,8')
  })

  it('"Afholdt i <måned>" renders the month-GET-derived usage (hours · days) with the lowercase Danish month', () => {
    renderStrip({
      monthAbsenceUsage: usage([
        ['VACATION', { hours: 14.8, days: 2 }],
        ['CARE_DAY', { hours: 7.4, days: 1 }],
      ]),
    })
    const ferieCard = screen.getByText('Ferie').closest('div') as HTMLElement
    expect(ferieCard.textContent).toContain('Afholdt i marts 14,8 t · 2 dage')
    const omsorgCard = screen.getByText('Omsorgsdage').closest('div') as HTMLElement
    expect(omsorgCard.textContent).toContain('Afholdt i marts 7,4 t · 1 dage')
    // No usage entry → zero line, not a crash
    const saerligCard = screen.getByText('Særlige feriedage').closest('div') as HTMLElement
    expect(saerligCard.textContent).toContain('Afholdt i marts 0 t · 0 dage')
  })
})

// ── The pure month-GET derivation helpers (useBalanceSummary) ──

describe('deriveMonthAbsenceUsage (R10)', () => {
  it('sums hours per type and SKIPS null-feriedage rows in the days sum (ADR-032 zero-norm days)', () => {
    const result = deriveMonthAbsenceUsage([
      { absenceType: 'VACATION', hours: 7.4, feriedage: 1 },
      { absenceType: 'VACATION', hours: 3.7, feriedage: 0.5 },
      // ADR-032 persists null feriedage on zero-norm days — hours count, days do NOT
      { absenceType: 'VACATION', hours: 2.0, feriedage: null },
      // absent field behaves like null (pre-7201 fixtures)
      { absenceType: 'CARE_DAY', hours: 7.4 },
    ])
    expect(result.get('VACATION')).toEqual({ hours: 13.1, days: 1.5 })
    expect(result.get('CARE_DAY')).toEqual({ hours: 7.4, days: 0 })
  })

  it('handles undefined input (no served absences)', () => {
    expect(deriveMonthAbsenceUsage(undefined).size).toBe(0)
  })

  it('S72 Step-7a Reviewer W1: SPECIAL_HOLIDAY_ALLOWANCE rows aggregate under the SPECIAL_HOLIDAY entitlement key (EntitlementMapping mirror)', () => {
    const result = deriveMonthAbsenceUsage([
      // the absence type the projection actually carries for særlige feriedage
      { absenceType: 'SPECIAL_HOLIDAY_ALLOWANCE', hours: 7.4, feriedage: 1 },
      { absenceType: 'SPECIAL_HOLIDAY', hours: 3.7, feriedage: 0.5 },
      // an unmapped type stays under its own key
      { absenceType: 'SICK_DAY', hours: 7.4 },
    ])
    expect(result.get('SPECIAL_HOLIDAY')).toEqual({ hours: 11.1, days: 1.5 })
    expect(result.has('SPECIAL_HOLIDAY_ALLOWANCE')).toBe(false)
    expect(result.get('SICK_DAY')).toEqual({ hours: 7.4, days: 0 })
  })
})

describe('computeMonthFlexDelta (R2 arithmetic — the grid Diff total mirror)', () => {
  const base = {
    year: 2026,
    month: 3,
    projectKeys: new Set(['DRIFT']),
    absenceKeys: new Set(['VACATION']),
    workIntervals: new Map(),
    manualHours: new Map(),
    dailyNorm: new Map<string, number | null>([
      ['2026-03-02', 7.4], // Monday
      ['2026-03-03', 7.4],
      ['2026-03-04', 7.4],
      ['2026-03-07', 0], // Saturday
    ]),
  }

  it('a full-absence day contributes 0,0 and a no-registration day contributes NOTHING (the two R2 pins)', () => {
    const delta = computeMonthFlexDelta({
      ...base,
      cellValues: new Map([['VACATION:2026-03-02', 7.4]]),
      // 2026-03-03 has NO registration → skipped (NOT −7,4)
    })
    expect(delta).toBe(0)
  })

  it('worked = intervals + manualHours against the served norm', () => {
    const delta = computeMonthFlexDelta({
      ...base,
      cellValues: new Map(),
      workIntervals: new Map([['2026-03-02', [{ start: '08:00', end: '15:00' }]]]), // 7.0
      manualHours: new Map([['2026-03-02', 1.0]]), // worked 8.0 vs 7.4 → +0.6
    })
    expect(delta).toBe(0.6)
  })

  it('days with a null/missing norm are skipped even when they carry data (R1)', () => {
    const delta = computeMonthFlexDelta({
      ...base,
      dailyNorm: new Map([['2026-03-02', null]]),
      cellValues: new Map([['DRIFT:2026-03-02', 5]]),
    })
    expect(delta).toBe(0)
  })

  it('an allocation-only day still registers against the norm (allocated counts as "has registration")', () => {
    const delta = computeMonthFlexDelta({
      ...base,
      cellValues: new Map([['DRIFT:2026-03-02', 5]]),
    })
    // has registration (allocated 5) but worked 0 + absence 0 − 7.4 = −7.4
    expect(delta).toBe(-7.4)
  })
})
