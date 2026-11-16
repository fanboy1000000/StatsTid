/// <reference types="vite/client" />
// S148 / TASK-14814, TASK-14844 — AC-S0-4a, AC-S0-4d, AC-S0-4e: one shared table of rest scenarios,
// `tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json` (schema 2), is judged by the
// engine in a C# fact (`RestParityTableTests`) and by Skema's instant warning here. Every row must
// get the same verdict on both sides, so a change to one side and not the other turns a test red.
//
// Every work end is judged on the 24 elapsed hours before it (Europe/Copenhagen); a finding is
// dated the day its work ends, one per day at the lowest value. The findings kept are those dated
// inside the period; the ones dated after it (a night shift over the month's last midnight, a rest
// ending on the 1st) are the ones named in `EXPECTED_DROPPED`, so the filter cannot hide a
// disagreement. The shown rest is `restDisplayHours` (rounded down once from the unrounded
// seconds); the expected side is keyed straight from the table's `restHours`, never through the
// helper. `derogationAllowed`, `projectHours`, `expectedEngineWarningDates` and
// `expectedEngineRestNotCheckedDays` are engine-only.
//
// Kinds per row. F (fail on the screen's earlier pair rule, which compared the gap between two
// days): the rows whose verdict moved under the look-back rule (split-day, long-shift,
// daylight-saving, seconds, rounding, month-cut and missing-hour rows, and the older rows that now
// date on the day the work ends). R (regression pins, green before and after): the rows with no
// finding on either side.
import { describe, it, expect } from 'vitest'
import { analyzeRestPeriods, restDisplayHours, type RestPeriodWarning } from '../useSkema'
import type { WorkTimeDay } from '../../types'

interface Scenario {
  id: string
  periodStart: string
  periodEnd: string
  intervals: { date: string; start: string; end: string }[]
  expectedDailyRest: { date: string; restHours: number }[]
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
  'rest-shown-is-rounded-down',
  'overlapping-intervals-are-one-work-block',
  'split-day-alone',
  'split-day-then-night-shift',
  'twenty-four-hour-shift',
  'exactly-thirteen-hours',
  'thirteen-hours-one-minute',
  'spring-forward-night-is-an-hour-shorter',
  'fall-back-night-is-an-hour-longer',
  'two-days-back-across-a-23-hour-day',
  'derogation-floor-eight-hours-is-a-warning',
  'derogation-below-floor-is-a-violation',
  'end-2359-is-the-end-of-the-day',
  'seconds-rest-rounds-once',
  'spring-forward-typed-0230-is-the-changeover',
  'fall-back-typed-0230-end-is-the-later-instant',
  'fall-back-typed-0230-start-is-the-earlier-instant',
  'night-shift-over-the-upper-month-edge',
  'spring-forward-work-inside-the-missing-hour-is-still-judged',
]

// The findings the screen computes but the period filter drops (dated after the period), named per
// row so the filter cannot hide a disagreement. Every other row drops nothing.
const EXPECTED_DROPPED: Record<string, string[]> = {
  'upper-month-edge-belongs-to-next-month': ['2026-04-01@9.0'],
  'night-shift-over-the-upper-month-edge': ['2026-04-01@6.0'],
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
  it('the table was read: schemaVersion 2 and its ids are exactly the thirty-three listed here', () => {
    expect(table, 'rest-parity-scenarios.json was not found by import.meta.glob').toBeDefined()
    expect(table!.schemaVersion).toBe(2)
    const ids = table!.scenarios.map((s) => s.id)
    expect([...ids].sort()).toEqual([...REQUIRED_IDS].sort())
  })

  for (const id of REQUIRED_IDS) {
    it(`${id}: the screen's findings equal expectedDailyRest`, () => {
      const s = table?.scenarios.find((x) => x.id === id)
      expect(s, `row ${id} missing`).toBeDefined()
      const sc = s!
      const inPeriod = (d: string) => d >= sc.periodStart && d <= sc.periodEnd
      // Every interval lies in the window the screen is fed: the two days before the period, the
      // period, and the day after it (the server's boundary days).
      for (const iv of sc.intervals) {
        expect(iv.date >= addDay(sc.periodStart, -2) && iv.date <= addDay(sc.periodEnd, 1), `${id}: ${iv.date} outside the fed window`).toBe(true)
      }
      const month = workDays(sc.intervals, inPeriod)
      const boundary = workDays(
        sc.intervals,
        (d) => d === addDay(sc.periodStart, -2) || d === addDay(sc.periodStart, -1) || d === addDay(sc.periodEnd, 1),
      )

      const byDate = new Map<string, RestPeriodWarning>()
      for (const date of datesOf(sc.periodStart, sc.periodEnd)) {
        for (const w of analyzeRestPeriods(month, boundary, date) ?? []) byDate.set(w.date, w)
      }
      const all = [...byDate.values()]
      const kept = all.filter((w) => inPeriod(w.date))
      const dropped = all.filter((w) => w.date > sc.periodEnd)

      // Actual side through the helper under test; expected side straight from the table's value.
      const shown = (w: RestPeriodWarning) => `${w.date}@${restDisplayHours(w.restSeconds).toFixed(1)}`
      expect(kept.map(shown).sort()).toEqual(
        sc.expectedDailyRest.map((r) => `${r.date}@${r.restHours.toFixed(1)}`).sort(),
      )
      // The filter must not hide a disagreement: the dropped findings are exactly the ones named.
      expect(dropped.map(shown).sort()).toEqual(EXPECTED_DROPPED[id] ?? [])
    })
  }
})
