/// <reference types="vite/client" />
// S148 / TASK-14814 — AC-S0-4a (the screen's half): one shared table of rest scenarios,
// `tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json`, is judged by the engine in a C#
// fact (`RestParityTableTests`) and by Skema's instant warning here. Every row must get the same
// verdict on both sides, so a change to one side and not the other turns a test red.
//
// A rest belongs to the period in which it ends (as on the server): the pairs kept are those whose
// later day is inside the period, and every pair dropped must end on the day after the period.
// `derogationAllowed`, `projectHours` and `expectedEngineRestNotCheckedDays` are engine-only.
//
// Kinds per row. F (fail on the code before the midnight join): `midnight-halves-alone`,
// `midnight-halves-then-same-day-work`, `midnight-halves-across-month-edge`,
// `continuation-day-without-other-work`, `chain-over-two-midnights`; and, before fix-up 1,
// `midpoint-rest-rounds-half-up` (shown 8,4 where the server says 8.5) and
// `overlapping-intervals-show-zero` (-1.0 where 0.0). R (regression pins, green before and after):
// `interior-short-rest`, `interior-short-rest-derogation`, `exactly-eleven-hours`,
// `partly-clocked-day`, `lower-month-edge-short-rest`, `upper-month-edge-belongs-to-next-month`,
// `reversed-interval-ignored`, and the two midnight-boundary rows `end-2358-does-not-join` and
// `start-0001-does-not-join` (each goes red if the join condition is loosened).
import { describe, it, expect } from 'vitest'
import { analyzeRestPeriods, restGapDisplayHours, type RestPeriodWarning } from '../useSkema'
import type { WorkTimeDay } from '../../types'

interface Scenario {
  id: string
  periodStart: string
  periodEnd: string
  intervals: { date: string; start: string; end: string }[]
  expectedDailyRest: { from: string; to: string; gapHours: number }[]
}
interface Table {
  schemaVersion: number
  scenarios: Scenario[]
}

const REQUIRED_IDS = [
  'interior-short-rest',
  'interior-short-rest-derogation',
  'midnight-halves-alone',
  'midnight-halves-then-same-day-work',
  'exactly-eleven-hours',
  'partly-clocked-day',
  'lower-month-edge-short-rest',
  'upper-month-edge-belongs-to-next-month',
  'midnight-halves-across-month-edge',
  'reversed-interval-ignored',
  'continuation-day-without-other-work',
  'chain-over-two-midnights',
  'end-2358-does-not-join',
  'start-0001-does-not-join',
  'midpoint-rest-rounds-half-up',
  'overlapping-intervals-show-zero',
]

// The pairs the screen computes but the period filter drops (a rest belongs to the month it ends
// in), named per row so the filter cannot hide a disagreement. Every other row drops nothing.
const EXPECTED_DROPPED: Record<string, string[]> = {
  'upper-month-edge-belongs-to-next-month': ['2026-03-31>2026-04-01@9.0'],
}

// The house rule is `import.meta.glob`, never `node:fs` (`clockGuard.test.ts`); the table lives
// outside `frontend/`, so the glob is tried first and the non-vacuity test below catches a miss.
const found = import.meta.glob('../../../../tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json', {
  eager: true,
  import: 'default',
}) as Record<string, Table>
const table: Table | undefined = Object.values(found)[0]

const addDay = (date: string, delta: number): string => {
  const d = new Date(date + 'T00:00:00Z')
  d.setUTCDate(d.getUTCDate() + delta)
  return d.toISOString().slice(0, 10)
}

function datesOf(from: string, to: string): string[] {
  const out: string[] = []
  for (let d = from; d <= to; d = addDay(d, 1)) out.push(d)
  return out
}

function workDays(intervals: Scenario['intervals'], keep: (date: string) => boolean): WorkTimeDay[] {
  const byDate = new Map<string, WorkTimeDay>()
  for (const iv of intervals) {
    if (!keep(iv.date)) continue
    const day = byDate.get(iv.date) ?? { date: iv.date, intervals: [] }
    day.intervals.push({ start: iv.start, end: iv.end })
    byDate.set(iv.date, day)
  }
  return [...byDate.values()]
}

describe('rest parity table — the screen judges every row as the engine does', () => {
  it('the table was read: schemaVersion 1 and its ids are exactly the sixteen listed here', () => {
    expect(table, 'rest-parity-scenarios.json was not found by import.meta.glob').toBeDefined()
    expect(table!.schemaVersion).toBe(1)
    const ids = table!.scenarios.map((s) => s.id)
    expect([...ids].sort()).toEqual([...REQUIRED_IDS].sort())
  })

  for (const id of REQUIRED_IDS) {
    it(`${id}: the screen's rests equal expectedDailyRest`, () => {
      const s = table?.scenarios.find((x) => x.id === id)
      expect(s, `row ${id} missing`).toBeDefined()
      const sc = s!
      const inPeriod = (d: string) => d >= sc.periodStart && d <= sc.periodEnd
      // Every interval lies in the window the screen is fed (the period and its two boundary days).
      for (const iv of sc.intervals) {
        expect(iv.date >= addDay(sc.periodStart, -1) && iv.date <= addDay(sc.periodEnd, 1), `${id}: ${iv.date} outside the fed window`).toBe(true)
      }
      const month = workDays(sc.intervals, inPeriod)
      const boundary = workDays(
        sc.intervals,
        (d) => d === addDay(sc.periodStart, -1) || d === addDay(sc.periodEnd, 1),
      )

      const pairs = new Map<string, RestPeriodWarning>()
      for (const date of datesOf(sc.periodStart, sc.periodEnd)) {
        for (const w of analyzeRestPeriods(month, boundary, date) ?? []) {
          pairs.set(`${w.fromDate}>${w.toDate}`, w)
        }
      }
      const all = [...pairs.values()]
      const kept = all.filter((w) => inPeriod(w.toDate))
      const dropped = all.filter((w) => !inPeriod(w.toDate))

      // The screen's figure is the shown one (one decimal, halves up), as the server's value is.
      const toKey = (from: string, to: string, gap: number) => `${from}>${to}@${restGapDisplayHours(gap).toFixed(1)}`
      expect(kept.map((w) => toKey(w.fromDate, w.toDate, w.gapHours)).sort()).toEqual(
        sc.expectedDailyRest.map((r) => toKey(r.from, r.to, r.gapHours)).sort(),
      )
      // The filter must not hide a disagreement: the dropped pairs are exactly the ones named.
      expect(dropped.map((w) => toKey(w.fromDate, w.toDate, w.gapHours)).sort()).toEqual(
        EXPECTED_DROPPED[id] ?? [],
      )
      for (const w of dropped) expect(w.toDate).toBe(addDay(sc.periodEnd, 1))
    })
  }
})
