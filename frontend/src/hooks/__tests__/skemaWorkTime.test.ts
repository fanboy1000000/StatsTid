// S72 / TASK-7203 — pure-function pins for the useSkema work-time helpers
// (SPRINT-72 R16: the hook module is the single owner of the workTime payload
// builder and the panel's period arithmetic). No DOM — everything here is a
// plain module-level function.
//
//   R7 — buildWorkTimePayload preserves the day's EXISTING manualHours through
//        a latest-wins write (THE pin lives in the hook, per R16).
//   R6 — analyzeRestPeriods: the §J adjacent-day 11-hour analysis, incl. the
//        served boundary days at month edges and the January year-boundary;
//        plus the 9t intra-day trigger threshold.
//   R5 — periodHours/periodsTotalHours keep EXACT 2-decimal arithmetic; the
//        0,1-rounding is display-only (the odd-minute pin).
import { describe, it, expect } from 'vitest'
import {
  analyzeRestPeriods,
  buildWorkTimePayload,
  computeDayDiffs,
  computeMonthDiffTotal,
  dayWorkedHours,
  deriveSkemaRowBasis,
  overlappingPeriodIndices,
  periodHours,
  periodsTotalHours,
  LONG_DAY_WARNING_HOURS,
} from '../useSkema'
import type { WorkTimeDay } from '../../types'

const wt = (date: string, intervals: { start: string; end: string }[], manualHours = 0): WorkTimeDay => ({
  date,
  intervals,
  manualHours,
})

describe('buildWorkTimePayload — R7 manualHours preservation (the hook-level pin)', () => {
  it('R7: a periods write for a day with EXISTING manualHours preserves the value in the payload (latest-wins write must not clobber it)', () => {
    const intervals = new Map([['2026-03-09', [{ start: '08:00', end: '12:00' }]]])
    const manual = new Map([['2026-03-09', 2.5]])
    const payload = buildWorkTimePayload(['2026-03-09'], intervals, manual)
    expect(payload).toEqual([
      {
        date: '2026-03-09',
        intervals: [{ start: '08:00', end: '12:00' }],
        manualHours: 2.5,
      },
    ])
  })

  it('R7: a day WITHOUT manualHours sends 0 (the save contract\'s "absent" value, unchanged from the shipped page)', () => {
    const intervals = new Map([['2026-03-10', [{ start: '09:00', end: '17:00' }]]])
    const payload = buildWorkTimePayload(['2026-03-10'], intervals, new Map())
    expect(payload[0].manualHours).toBe(0)
  })

  it('emits one entry per dirty date — a manual-only day round-trips its manualHours with empty intervals', () => {
    const intervals = new Map([['2026-03-09', [{ start: '08:00', end: '12:00' }]]])
    const manual = new Map([['2026-03-11', 7.4]])
    const payload = buildWorkTimePayload(['2026-03-09', '2026-03-11'], intervals, manual)
    expect(payload).toHaveLength(2)
    expect(payload[1]).toEqual({ date: '2026-03-11', intervals: [], manualHours: 7.4 })
  })

  it('copies interval objects into the payload (no shared references with the live map)', () => {
    const source = { start: '08:00', end: '12:00' }
    const intervals = new Map([['2026-03-09', [source]]])
    const payload = buildWorkTimePayload(['2026-03-09'], intervals, new Map())
    expect(payload[0].intervals[0]).toEqual(source)
    expect(payload[0].intervals[0]).not.toBe(source)
  })
})

describe('periodHours / periodsTotalHours / dayWorkedHours — R5 exact arithmetic', () => {
  it('computes single-period hours at exact 2 decimals (10:27–13:03 = 2.6)', () => {
    expect(periodHours({ from: '10:27', to: '13:03' })).toBe(2.6)
  })

  it('R5 odd-minute pin: 08:00–11:47 keeps the EXACT 2-decimal value 3.78 (the 0,1 display rounding never enters the arithmetic)', () => {
    expect(periodHours({ from: '08:00', to: '11:47' })).toBe(3.78)
  })

  it('returns null for reversed, zero-length, and unparsable periods ("ugyldig")', () => {
    expect(periodHours({ from: '13:00', to: '10:00' })).toBeNull() // reversed
    expect(periodHours({ from: '10:00', to: '10:00' })).toBeNull() // zero-length
    expect(periodHours({ from: '99:00', to: '12:00' })).toBeNull() // invalid hour
    expect(periodHours({ from: '', to: '12:00' })).toBeNull() // incomplete
    expect(periodHours({ from: 'abc', to: '12:00' })).toBeNull() // garbage
  })

  it('accepts the prototype raw forms ("9:05", "09.05", "0905") and the served "HH:mm:ss"', () => {
    expect(periodHours({ from: '9:05', to: '12:05' })).toBe(3)
    expect(periodHours({ from: '09.05', to: '12:05' })).toBe(3)
    expect(periodHours({ from: '0905', to: '1205' })).toBe(3)
    expect(periodHours({ from: '09:05:00', to: '12:05:00' })).toBe(3)
  })

  it('periodsTotalHours sums exact MINUTES and rounds ONCE at the end (never a sum of per-row-rounded values)', () => {
    // 3 × 50 min = 150 min = 2.5 exactly; per-row 2-dec rounding (0.83 × 3 = 2.49) would drift.
    const periods = [
      { from: '08:00', to: '08:50' },
      { from: '09:00', to: '09:50' },
      { from: '10:00', to: '10:50' },
    ]
    expect(periodsTotalHours(periods)).toBe(2.5)
  })

  it('periodsTotalHours ignores invalid/reversed rows', () => {
    const periods = [
      { from: '08:00', to: '12:00' },
      { from: '15:00', to: '13:00' }, // reversed — ignored
      { from: '', to: '' }, // blank — ignored
    ]
    expect(periodsTotalHours(periods)).toBe(4)
  })

  it('dayWorkedHours adds the day\'s existing manual hours to the period total (D-B: manual hours keep counting in worked totals)', () => {
    expect(dayWorkedHours([{ from: '08:00', to: '12:00' }], 2.5)).toBe(6.5)
    expect(dayWorkedHours([], 7.4)).toBe(7.4)
  })

  it('R6: the 9t intra-day trigger fires strictly ABOVE 9 worked hours (9,0 is quiet)', () => {
    expect(dayWorkedHours([{ from: '08:00', to: '17:00' }], 0)).toBe(9)
    expect(dayWorkedHours([{ from: '08:00', to: '17:00' }], 0) > LONG_DAY_WARNING_HOURS).toBe(false)
    expect(dayWorkedHours([{ from: '08:00', to: '17:06' }], 0) > LONG_DAY_WARNING_HOURS).toBe(true)
  })
})

describe('overlappingPeriodIndices — the S58 overlap mirror', () => {
  it('flags BOTH rows of an overlapping pair', () => {
    const out = overlappingPeriodIndices([
      { from: '08:00', to: '12:00' },
      { from: '11:00', to: '15:00' },
    ])
    expect(out).toEqual(new Set([0, 1]))
  })

  it('allows touching boundaries (next.from === prev.to) — mirrors the backend guard', () => {
    const out = overlappingPeriodIndices([
      { from: '08:00', to: '12:00' },
      { from: '12:00', to: '16:00' },
    ])
    expect(out.size).toBe(0)
  })

  it('catches a chain overlap through the running max end (9–17 swallows 16–18 even after 10–11)', () => {
    const out = overlappingPeriodIndices([
      { from: '09:00', to: '17:00' },
      { from: '10:00', to: '11:00' },
      { from: '16:00', to: '18:00' },
    ])
    expect(out).toEqual(new Set([0, 1, 2]))
  })

  it('ignores invalid/reversed rows entirely', () => {
    const out = overlappingPeriodIndices([
      { from: '08:00', to: '12:00' },
      { from: '15:00', to: '09:00' }, // reversed — never "overlaps"
    ])
    expect(out.size).toBe(0)
  })
})

describe('analyzeRestPeriods — R6 §J adjacent-day 11-hour analysis (pure)', () => {
  it('R6: warns on a month-interior gap below 11h between the previous day\'s last end and this day\'s first start', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '23:00' }]),
      wt('2026-03-09', [{ start: '07:00', end: '15:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toEqual([
      { gapHours: 8, fromDate: '2026-03-08', toDate: '2026-03-09' },
    ])
  })

  it('R6: a gap of exactly 11 hours does NOT warn (the minimum is met)', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '20:00' }]),
      wt('2026-03-09', [{ start: '07:00', end: '15:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toBeNull()
  })

  it('R6: the symmetric NEXT-day trigger warns (this day ends late, the next starts early)', () => {
    const month = [
      wt('2026-03-09', [{ start: '14:00', end: '23:00' }]),
      wt('2026-03-10', [{ start: '08:00', end: '16:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toEqual([
      { gapHours: 9, fromDate: '2026-03-09', toDate: '2026-03-10' },
    ])
  })

  it('R6: both sides can warn at once (previous-gap first)', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '23:30' }]),
      wt('2026-03-09', [{ start: '06:00', end: '23:00' }]),
      wt('2026-03-10', [{ start: '05:00', end: '13:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toEqual([
      { gapHours: 6.5, fromDate: '2026-03-08', toDate: '2026-03-09' },
      { gapHours: 6, fromDate: '2026-03-09', toDate: '2026-03-10' },
    ])
  })

  it('R6 month-EDGE: the previous day resolves from the served boundaryWorkTime (date = the 1st)', () => {
    const boundary = [wt('2026-02-28', [{ start: '14:00', end: '22:00' }])]
    const month = [wt('2026-03-01', [{ start: '06:00', end: '14:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-03-01')).toEqual([
      { gapHours: 8, fromDate: '2026-02-28', toDate: '2026-03-01' },
    ])
  })

  it('R6 month-EDGE: the next day resolves from the served boundaryWorkTime (date = the last of the month)', () => {
    const boundary = [wt('2026-04-01', [{ start: '06:00', end: '14:00' }])]
    const month = [wt('2026-03-31', [{ start: '14:00', end: '22:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-03-31')).toEqual([
      { gapHours: 8, fromDate: '2026-03-31', toDate: '2026-04-01' },
    ])
  })

  it('R6 January year-boundary: 2026-01-01\'s previous day is 2025-12-31 (boundary-served)', () => {
    const boundary = [wt('2025-12-31', [{ start: '15:00', end: '23:00' }])]
    const month = [wt('2026-01-01', [{ start: '08:00', end: '16:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-01-01')).toEqual([
      { gapHours: 9, fromDate: '2025-12-31', toDate: '2026-01-01' },
    ])
  })

  it('R6 no-data days: null when this day has no intervals (nothing to anchor)', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '23:59' }]),
      wt('2026-03-09', [], 7.4), // manual-only day — no clock anchor
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toBeNull()
    expect(analyzeRestPeriods(month, [], '2026-03-10')).toBeNull() // entirely unserved day
  })

  it('R6 no-data days: an adjacent day without interval data imposes no constraint', () => {
    const month = [wt('2026-03-09', [{ start: '00:30', end: '23:30' }])]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toBeNull()
  })

  it('uses the FIRST start / LAST end across multiple (unordered) intervals and parses the served "HH:mm:ss" format', () => {
    const month = [
      wt('2026-03-08', [
        { start: '08:00:00', end: '12:00:00' },
        { start: '13:00:00', end: '22:30:00' }, // last end 22:30
      ]),
      wt('2026-03-09', [
        { start: '12:00', end: '16:00' },
        { start: '07:00', end: '11:00' }, // first start 07:00 (unordered input)
      ]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09')).toEqual([
      { gapHours: 8.5, fromDate: '2026-03-08', toDate: '2026-03-09' },
    ])
  })

  it('month entries win over a boundary duplicate for the same date', () => {
    // The boundary copy would warn (ends 23:00); the month copy (ends 18:00) must win.
    const boundary = [wt('2026-03-08', [{ start: '12:00', end: '23:00' }])]
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '18:00' }]),
      wt('2026-03-09', [{ start: '07:00', end: '15:00' }]),
    ]
    expect(analyzeRestPeriods(month, boundary, '2026-03-09')).toBeNull()
  })
})

// ── S72 Step-7a fix-forward (B1/W1) — the union row basis + the R2 single owner ──

describe('deriveSkemaRowBasis — the R3/R12 union basis (Step-7a B1)', () => {
  const data = {
    // The legacy fields = the VISIBLE selection (the 7201 configured-user contract)
    projects: [
      { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 1 },
    ],
    absenceTypes: [{ type: 'VACATION', label: 'Ferie' }],
    entries: [
      { date: '2026-03-02', projectCode: 'UDV', hours: 2.1 },
      { date: '2026-03-05', projectCode: 'GAMMEL', hours: 4 }, // deactivated, catalog-less
    ],
    absences: [{ date: '2026-03-03', absenceType: 'OLD_TYPE', hours: 7.4 }],
    catalogs: {
      projects: [
        { projectId: 'p-drift', projectCode: 'DRIFT', projectName: 'Drift & support', sortOrder: 0 },
        { projectId: 'p-udv', projectCode: 'UDV', projectName: 'Udvikling', sortOrder: 1 },
      ],
      absenceTypes: [
        { type: 'VACATION', label: 'Ferie' },
        { type: 'CARE_DAY', label: 'Omsorgsdage' },
      ],
    },
  }

  it('B1: unions catalogs ∪ the visible selection ∪ served entry/absence keys — hidden and deactivated keys are IN the basis; deactivated keys label by code; no duplicates', () => {
    const basis = deriveSkemaRowBasis(data)
    expect([...basis.projectKeys]).toEqual(['DRIFT', 'UDV', 'GAMMEL'])
    expect([...basis.absenceKeys]).toEqual(['VACATION', 'CARE_DAY', 'OLD_TYPE'])
    const gammel = basis.rows.find((r) => r.key === 'GAMMEL')
    expect(gammel).toEqual({ type: 'project', key: 'GAMMEL', label: 'GAMMEL' }) // labeled by code
    const oldType = basis.rows.find((r) => r.key === 'OLD_TYPE')
    expect(oldType).toEqual({ type: 'absence', key: 'OLD_TYPE', label: 'OLD_TYPE' })
    // catalog labels win for keys the catalog knows
    expect(basis.rows.find((r) => r.key === 'DRIFT')?.label).toBe('Drift & support')
    // projects first, then absences; one row per key
    expect(basis.rows.map((r) => r.key)).toEqual(['DRIFT', 'UDV', 'GAMMEL', 'VACATION', 'CARE_DAY', 'OLD_TYPE'])
  })

  it('pre-7201 fallback: without catalogs the legacy fields + served keys form the basis', () => {
    const basis = deriveSkemaRowBasis({ ...data, catalogs: undefined })
    expect([...basis.projectKeys]).toEqual(['UDV', 'GAMMEL'])
    expect([...basis.absenceKeys]).toEqual(['VACATION', 'OLD_TYPE'])
  })
})

describe('computeDayDiffs / computeMonthDiffTotal — the R2 single computation (W1)', () => {
  const base = {
    year: 2026,
    month: 3,
    projectKeys: new Set(['DRIFT']),
    absenceKeys: new Set(['VACATION']),
    workIntervals: new Map(),
    manualHours: new Map(),
    dailyNorm: new Map<string, number | null>([
      ['2026-03-02', 7.4],
      ['2026-03-03', 7.4],
      ['2026-03-04', null], // academic ANNUAL_ACTIVITY
    ]),
  }

  it('R2: full-absence day → 0; no-registration day → ABSENT; null-norm day → ABSENT even with data; the total sums the rendered values only', () => {
    const diffs = computeDayDiffs({
      ...base,
      cellValues: new Map([
        ['VACATION:2026-03-02', 7.4], // full absence → 0,0
        ['DRIFT:2026-03-04', 5], // null norm → absent (R1)
        // 2026-03-03 has NO registration → absent (NOT −7,4)
      ]),
    })
    expect(diffs.get('2026-03-02')).toBe(0)
    expect(diffs.has('2026-03-03')).toBe(false)
    expect(diffs.has('2026-03-04')).toBe(false)
    expect(computeMonthDiffTotal(diffs)).toBe(0)
  })

  it('R2: worked = intervals + manualHours against the served norm, 2-decimal', () => {
    const diffs = computeDayDiffs({
      ...base,
      cellValues: new Map(),
      workIntervals: new Map([['2026-03-02', [{ start: '08:00', end: '15:00' }]]]), // 7.0
      manualHours: new Map([['2026-03-02', 1.0]]), // worked 8.0 vs 7.4 → +0.6
    })
    expect(diffs.get('2026-03-02')).toBe(0.6)
    expect(computeMonthDiffTotal(diffs)).toBe(0.6)
  })
})
