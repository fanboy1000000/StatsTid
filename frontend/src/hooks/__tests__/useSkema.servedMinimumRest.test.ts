// S148 / TASK-14829 — AC-S0-4c: Skema's instant rest warning judges each work end by the minimum rest
// the server serves for that end's own date (RR-7), not a fixed 11 hours.
//
// Kind F. Red conditions: before the change `analyzeRestPeriods` ignores its fourth argument and
// compares every end with 11 h, so Monday 16 March's 11,5 h rest does not warn under a served 12
// (first arm, and the Saturday-to-Sunday arm), and the 12-then-11 arm and the null arm still warn
// or fail on a day the served value says otherwise. The null arm also pins BC-8 (a): a date with no
// served value is not judged.
//
// March 2026: 14 = Saturday, 15 = Sunday, 16 = Monday.
import { describe, it, expect } from 'vitest'
import { analyzeRestPeriods } from '../useSkema'
import type { WorkTimeDay } from '../../types'

const wt = (date: string, intervals: { start: string; end: string }[]): WorkTimeDay => ({ date, intervals })

const served = (byDate: Record<string, number | null>) => (date: string) => byDate[date] ?? null

describe('analyzeRestPeriods — the served minimum rest, per judged end', () => {
  const sundayThenMonday = [
    wt('2026-03-15', [{ start: '10:00', end: '20:30' }]),
    wt('2026-03-16', [{ start: '08:00', end: '14:00' }]),
  ]

  it('served 11 for Sunday and 12 for Monday: Monday warns at 11,5 h, Sunday does not', () => {
    const minimum = served({ '2026-03-15': 11, '2026-03-16': 12 })
    expect(analyzeRestPeriods(sundayThenMonday, [], '2026-03-16', minimum)).toEqual([
      { date: '2026-03-16', endsAt: '14:00', restSeconds: 41400, minimumHours: 12 },
    ])
    // Opened on Sunday the Monday finding also shows (its window holds Sunday's work); Sunday's own end has none.
    expect(analyzeRestPeriods(sundayThenMonday, [], '2026-03-15', minimum)?.map((w) => w.date)).toEqual(['2026-03-16'])
  })

  it('served 12 for Sunday and 11 for Monday: Monday does not warn (its own date decides)', () => {
    const minimum = served({ '2026-03-15': 12, '2026-03-16': 11 })
    expect(analyzeRestPeriods(sundayThenMonday, [], '2026-03-16', minimum)).toBeNull()
  })

  it('a Sunday work end with 11,5 h of rest warns when Sunday is served 12', () => {
    const month = [
      wt('2026-03-14', [{ start: '10:00', end: '20:30' }]),
      wt('2026-03-15', [{ start: '08:00', end: '14:00' }]),
    ]
    const minimum = served({ '2026-03-14': 11, '2026-03-15': 12, '2026-03-16': 11 })
    expect(analyzeRestPeriods(month, [], '2026-03-15', minimum)).toEqual([
      { date: '2026-03-15', endsAt: '14:00', restSeconds: 41400, minimumHours: 12 },
    ])
  })

  it('a date served null (or absent) is not judged', () => {
    expect(analyzeRestPeriods(sundayThenMonday, [], '2026-03-16', served({ '2026-03-16': null }))).toBeNull()
    expect(analyzeRestPeriods(sundayThenMonday, [], '2026-03-16', () => null)).toBeNull()
  })
})
