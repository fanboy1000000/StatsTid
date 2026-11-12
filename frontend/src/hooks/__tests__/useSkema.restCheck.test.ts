// S148 / TASK-14814 — AC-S0-4b: Skema's instant rest warning treats a night shift typed across
// midnight (22:00-23:59 + 00:00-06:00; Skema cannot type 24:00) as ONE shift, as the server does.
// The rests are judged the engine's way: shifts joined at midnight, grouped by the day they start,
// consecutive groups compared (`analyzeRestPeriods`).
//
// Kind F. Red conditions: before this task the one-minute gap at midnight warns (0,02 h), so
// `MidnightHalves_NoWarning` fails at its first `toBeNull` (received a warning with gapHours 0.02);
// the after-06:00 arm fails because the 0,02 h midnight warning is returned beside the 10 h one;
// the month-edge arm fails the same way; the two run-on arms fail because the per-day model labels
// the rest from the wrong day (or finds none).
import { describe, it, expect } from 'vitest'
import { analyzeRestPeriods } from '../useSkema'
import type { WorkTimeDay } from '../../types'

const wt = (date: string, intervals: { start: string; end: string }[]): WorkTimeDay => ({
  date,
  intervals,
})

describe("analyzeRestPeriods — midnight continuity (the engine's algorithm)", () => {
  it('MidnightHalves_NoWarning: 22:00-23:59 + 00:00-06:00 raises no warning on either day', () => {
    const month = [
      wt('2026-03-11', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-12', [{ start: '00:00', end: '06:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-11')).toBeNull()
    expect(analyzeRestPeriods(month, [], '2026-03-12')).toBeNull()
  })

  it('the rest after the night shift is still caught: 06:00 to 16:00 is 10 h, from either day', () => {
    const month = [
      wt('2026-03-11', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-12', [
        { start: '00:00', end: '06:00' },
        { start: '16:00', end: '20:00' },
      ]),
    ]
    const expected = [{ gapHours: 10, fromDate: '2026-03-11', toDate: '2026-03-12' }]
    expect(analyzeRestPeriods(month, [], '2026-03-11')).toEqual(expected)
    expect(analyzeRestPeriods(month, [], '2026-03-12')).toEqual(expected)
  })

  it('a night shift over the month edge (boundary 31 Mar + 1 Apr) raises no warning for 1 April', () => {
    const boundary = [wt('2026-03-31', [{ start: '22:00', end: '23:59' }])]
    const month = [wt('2026-04-01', [{ start: '00:00', end: '06:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-04-01')).toBeNull()
  })

  it('continuation day without other work: the shift belongs to the day it started', () => {
    const month = [
      wt('2026-03-10', [{ start: '20:00', end: '23:59' }]),
      wt('2026-03-11', [{ start: '00:00', end: '20:00' }]),
      wt('2026-03-12', [{ start: '05:00', end: '13:00' }]),
    ]
    const expected = [{ gapHours: 9, fromDate: '2026-03-10', toDate: '2026-03-12' }]
    for (const d of ['2026-03-10', '2026-03-11', '2026-03-12']) {
      expect(analyzeRestPeriods(month, [], d)).toEqual(expected)
    }
  })

  it('chain over two midnights is one shift', () => {
    const month = [
      wt('2026-03-09', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-10', [{ start: '00:00', end: '23:59' }]),
      wt('2026-03-11', [
        { start: '00:00', end: '02:00' },
        { start: '10:00', end: '14:00' },
      ]),
    ]
    const expected = [{ gapHours: 8, fromDate: '2026-03-09', toDate: '2026-03-11' }]
    for (const d of ['2026-03-09', '2026-03-10', '2026-03-11']) {
      expect(analyzeRestPeriods(month, [], d)).toEqual(expected)
    }
  })
})
