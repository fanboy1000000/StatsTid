// S72 / TASK-7203 — pure-function pins for the useSkema work-time helpers
// (SPRINT-72 R16: the hook module is the single owner of the workTime payload
// builder and the panel's period arithmetic). No DOM — everything here is a
// plain module-level function.
//
//   buildWorkTimePayload emits each day as exactly { date, intervals } — clock
//        time only (manual hours are retired, ADR-043 D10).
//   R6 — analyzeRestPeriods: the 24-hour look-back 11-hour analysis, incl. the
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
  deriveSkemaRowBasis,
  overlappingPeriodIndices,
  periodHours,
  periodsTotalHours,
  restDisplayHours,
  LONG_DAY_WARNING_HOURS,
} from '../useSkema'
import type { WorkTimeDay } from '../../types'

const wt = (date: string, intervals: { start: string; end: string }[]): WorkTimeDay => ({
  date,
  intervals,
})

describe('buildWorkTimePayload — the clock-time-only payload shape', () => {
  it('emits one entry per dirty date, each exactly { date, intervals } (a date without intervals sends an empty list)', () => {
    const intervals = new Map([['2026-03-09', [{ start: '08:00', end: '12:00' }]]])
    const payload = buildWorkTimePayload(['2026-03-09', '2026-03-11'], intervals)
    expect(payload).toEqual([
      { date: '2026-03-09', intervals: [{ start: '08:00', end: '12:00' }] },
      { date: '2026-03-11', intervals: [] },
    ])
  })

  it('copies interval objects into the payload (no shared references with the live map)', () => {
    const source = { start: '08:00', end: '12:00' }
    const intervals = new Map([['2026-03-09', [source]]])
    const payload = buildWorkTimePayload(['2026-03-09'], intervals)
    expect(payload[0].intervals[0]).toEqual(source)
    expect(payload[0].intervals[0]).not.toBe(source)
  })
})

describe('periodHours / periodsTotalHours — R5 exact arithmetic', () => {
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

  it('R6: the 9t intra-day trigger fires strictly ABOVE 9 worked hours (9,0 is quiet)', () => {
    expect(periodsTotalHours([{ from: '08:00', to: '17:00' }])).toBe(9)
    expect(periodsTotalHours([{ from: '08:00', to: '17:00' }]) > LONG_DAY_WARNING_HOURS).toBe(false)
    expect(periodsTotalHours([{ from: '08:00', to: '17:06' }]) > LONG_DAY_WARNING_HOURS).toBe(true)
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

// S148 3b (RR-1) — the look-back rule: every work end is judged on the 24 elapsed hours before it
// (Europe/Copenhagen), one finding per day at the lowest value, dated the day the work ends. A
// finding is `{ date, endsAt, restSeconds, minimumHours }`, the rest unrounded. Kind F for every case whose
// value or shape moved (the pair rule returned `{ gapHours, fromDate, toDate }`); the null cases
// are R pins.
describe('analyzeRestPeriods — RR-1 the 24 hours before each work end (pure)', () => {
  it('R6: warns when the longest rest in the 24 hours before a work end is below 11h (dated the day the work ends)', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '23:00' }]),
      wt('2026-03-09', [{ start: '07:00', end: '15:00' }]),
    ]
    // S148 3b (RR-1): { gapHours: 8, fromDate: '2026-03-08', toDate: '2026-03-09' } → { date, endsAt, restSeconds, minimumHours }
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toEqual([
      { date: '2026-03-09', endsAt: '15:00', restSeconds: 28800, minimumHours: 11 },
    ])
  })

  it('R6: a rest of exactly 11 hours does NOT warn (the minimum is met)', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '20:00' }]),
      wt('2026-03-09', [{ start: '07:00', end: '15:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toBeNull()
  })

  it('R6: the earlier day also warns (its work lies inside the judged window), dated the later day', () => {
    const month = [
      wt('2026-03-09', [{ start: '14:00', end: '23:00' }]),
      wt('2026-03-10', [{ start: '08:00', end: '16:00' }]),
    ]
    // S148 3b (RR-1): { gapHours: 9, fromDate: '2026-03-09', toDate: '2026-03-10' } → { date, endsAt, restSeconds, minimumHours }
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toEqual([
      { date: '2026-03-10', endsAt: '16:00', restSeconds: 32400, minimumHours: 11 },
    ])
  })

  it('R6: two findings on one open day, ordered by the day the work ends', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '23:30' }]),
      wt('2026-03-09', [{ start: '06:00', end: '23:00' }]),
      wt('2026-03-10', [{ start: '05:00', end: '13:00' }]),
    ]
    // S148 3b (RR-1): two pairs (6,5 and 6) → two findings, the 9th's 23:00 end and the 10th's 13:00 end
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toEqual([
      { date: '2026-03-09', endsAt: '23:00', restSeconds: 23400, minimumHours: 11 },
      { date: '2026-03-10', endsAt: '13:00', restSeconds: 21600, minimumHours: 11 },
    ])
  })

  it('R6 month-EDGE: the previous day resolves from the served boundaryWorkTime (date = the 1st)', () => {
    const boundary = [wt('2026-02-28', [{ start: '14:00', end: '22:00' }])]
    const month = [wt('2026-03-01', [{ start: '06:00', end: '14:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-03-01', () => 11)).toEqual([
      { date: '2026-03-01', endsAt: '14:00', restSeconds: 28800, minimumHours: 11 },
    ])
  })

  it('R6 month-EDGE: the next day resolves from the served boundaryWorkTime (date = the last of the month)', () => {
    const boundary = [wt('2026-04-01', [{ start: '06:00', end: '14:00' }])]
    const month = [wt('2026-03-31', [{ start: '14:00', end: '22:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-03-31', () => 11)).toEqual([
      { date: '2026-04-01', endsAt: '14:00', restSeconds: 28800, minimumHours: 11 },
    ])
  })

  it('R6 January year-boundary: 2026-01-01\'s previous day is 2025-12-31 (boundary-served)', () => {
    const boundary = [wt('2025-12-31', [{ start: '15:00', end: '23:00' }])]
    const month = [wt('2026-01-01', [{ start: '08:00', end: '16:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-01-01', () => 11)).toEqual([
      { date: '2026-01-01', endsAt: '16:00', restSeconds: 32400, minimumHours: 11 },
    ])
  })

  it('R6 no-data days: null when this day has no intervals (nothing to anchor)', () => {
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '23:59' }]),
      wt('2026-03-09', []), // a day without intervals — no clock anchor
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toBeNull()
    expect(analyzeRestPeriods(month, [], '2026-03-10', () => 11)).toBeNull() // entirely unserved day
  })

  // S148 3b (RR-1): was "an adjacent day without interval data imposes no constraint" (null). The free
  // adjacent days are still rest, but 00:30-23:30 is a 23-hour day: the only rest in the 24 hours
  // before 23:30 is 23:30-00:30, one hour, a breach on its own.
  it('a free adjacent day is rest, but a 23-hour day is a breach on its own (1 hour of rest before 23:30)', () => {
    const month = [wt('2026-03-09', [{ start: '00:30', end: '23:30' }])]
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toEqual([
      { date: '2026-03-09', endsAt: '23:30', restSeconds: 3600, minimumHours: 11 },
    ])
  })

  it('judges every interval of a day and parses the served "HH:mm:ss" format; equal lowest values keep the earliest end', () => {
    const month = [
      wt('2026-03-08', [
        { start: '08:00:00', end: '12:00:00' },
        { start: '13:00:00', end: '22:30:00' },
      ]),
      wt('2026-03-09', [
        { start: '12:00', end: '16:00' },
        { start: '07:00', end: '11:00' }, // unordered input
      ]),
    ]
    // The 11:00 and 16:00 ends both see 8,5 h (22:30-07:00); the day's one finding is the earlier end.
    expect(analyzeRestPeriods(month, [], '2026-03-09', () => 11)).toEqual([
      { date: '2026-03-09', endsAt: '11:00', restSeconds: 30600, minimumHours: 11 },
    ])
  })

  it('month entries win over a boundary duplicate for the same date', () => {
    // The boundary copy would warn (ends 23:00); the month copy (ends 18:00) must win.
    const boundary = [wt('2026-03-08', [{ start: '12:00', end: '23:00' }])]
    const month = [
      wt('2026-03-08', [{ start: '12:00', end: '18:00' }]),
      wt('2026-03-09', [{ start: '07:00', end: '15:00' }]),
    ]
    expect(analyzeRestPeriods(month, boundary, '2026-03-09', () => 11)).toBeNull()
  })
})

describe('restDisplayHours — the shown rest, rounded down once from the unrounded seconds', () => {
  // Kind N (a function this task creates). Red condition: the old half-up helper, or a two-step
  // rounding, shows 8,5 for 30 590 s and 11 for 39 540 s.
  it.each([
    [30420, 8.4], // 8 h 27 min
    [30590, 8.4], // 8 h 29 min 50 s: two decimals first would give 8,5
    [39540, 10.9], // 10 h 59 min: a breach never shows the minimum itself
    [3600, 1],
    [0, 0],
  ])('%i s shows %f', (seconds, shown) => {
    expect(restDisplayHours(seconds)).toBe(shown)
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

  it('R2: worked = clock intervals against the served norm, 2-decimal', () => {
    const diffs = computeDayDiffs({
      ...base,
      cellValues: new Map(),
      workIntervals: new Map([['2026-03-02', [{ start: '08:00', end: '16:00' }]]]), // worked 8.0 vs 7.4 → +0.6
    })
    expect(diffs.get('2026-03-02')).toBe(0.6)
    expect(computeMonthDiffTotal(diffs)).toBe(0.6)
  })
})
