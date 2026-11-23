// S148 / TASK-14814, TASK-14844 — AC-S0-4b, AC-S0-4f: Skema's instant rest warning treats a night
// shift typed across midnight (22:00-23:59 + 00:00-06:00; Skema cannot type 24:00) as ONE shift, as
// the server does, judges every work end on the 24 elapsed hours before it (`analyzeRestPeriods`),
// and shows a finding on the days that contributed to it: the day the work ends, every day of the
// judged stint, and every day with work inside its window.
//
// Kind F. Red conditions: before the look-back rule the findings have the shape
// `{ gapHours, fromDate, toDate }`, so every `toEqual` below fails on its first key; the marking
// arms fail because a 24-hour shift finds no pair and the earlier day's warning names the wrong
// day; the month-cut arm shows only the 1 April finding. The null arms are R pins (AC-S0-4b holds).
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
    expect(analyzeRestPeriods(month, [], '2026-03-11', () => 11)).toBeNull()
    expect(analyzeRestPeriods(month, [], '2026-03-12', () => 11)).toBeNull()
  })

  it('the rest after the night shift is still caught: 06:00 to 16:00 is 10 h, from either day', () => {
    const month = [
      wt('2026-03-11', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-12', [
        { start: '00:00', end: '06:00' },
        { start: '16:00', end: '20:00' },
      ]),
    ]
    // S148 3b (RR-1): { gapHours: 10, fromDate: '2026-03-11', toDate: '2026-03-12' } → { date, endsAt, restSeconds, minimumHours }
    const expected = [{ date: '2026-03-12', endsAt: '20:00', restSeconds: 36000, minimumHours: 11 }]
    expect(analyzeRestPeriods(month, [], '2026-03-11', () => 11)).toEqual(expected)
    expect(analyzeRestPeriods(month, [], '2026-03-12', () => 11)).toEqual(expected)
  })

  it('a night shift over the month edge (boundary 31 Mar + 1 Apr) raises no warning for 1 April', () => {
    const boundary = [wt('2026-03-31', [{ start: '22:00', end: '23:59' }])]
    const month = [wt('2026-04-01', [{ start: '00:00', end: '06:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-04-01', () => 11)).toBeNull()
  })

  it('continuation day without other work: the shift is judged at its end, and each day sees what it carries', () => {
    const month = [
      wt('2026-03-10', [{ start: '20:00', end: '23:59' }]),
      wt('2026-03-11', [{ start: '00:00', end: '20:00' }]),
      wt('2026-03-12', [{ start: '05:00', end: '13:00' }]),
    ]
    // S148 3b (RR-1): one pair (9 h, 10th to 12th) → the 24-hour shift's 0 h on the 11th and the 12th's 9 h
    const shift = { date: '2026-03-11', endsAt: '20:00', restSeconds: 0, minimumHours: 11 }
    const next = { date: '2026-03-12', endsAt: '13:00', restSeconds: 32400, minimumHours: 11 }
    expect(analyzeRestPeriods(month, [], '2026-03-10', () => 11)).toEqual([shift])
    expect(analyzeRestPeriods(month, [], '2026-03-11', () => 11)).toEqual([shift, next])
    expect(analyzeRestPeriods(month, [], '2026-03-12', () => 11)).toEqual([next])
  })

  // S148 3b (RR-1): was `[gap 0]`. Overlapping intervals are one block of work; no window fails.
  it('overlapping intervals are one work block and raise no warning', () => {
    const month = [
      wt('2026-03-25', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-26', [
        { start: '00:00', end: '06:00' },
        { start: '05:00', end: '09:00' },
      ]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-26', () => 11)).toBeNull()
  })

  it('chain over two midnights is one shift, shown from its first day', () => {
    const month = [
      wt('2026-03-09', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-10', [{ start: '00:00', end: '23:59' }]),
      wt('2026-03-11', [
        { start: '00:00', end: '02:00' },
        { start: '10:00', end: '14:00' },
      ]),
    ]
    // S148 3b (RR-1): [gap 8, 9th to 11th] → the chain's 0 h at its end, 02:00 on the 11th
    const expected = [{ date: '2026-03-11', endsAt: '02:00', restSeconds: 0, minimumHours: 11 }]
    for (const d of ['2026-03-09', '2026-03-10', '2026-03-11']) {
      expect(analyzeRestPeriods(month, [], d, () => 11)).toEqual(expected)
    }
  })
})

describe('analyzeRestPeriods — which days show a finding (AC-S0-4f)', () => {
  it('a 24-hour shift is shown from its first day', () => {
    const month = [
      wt('2026-03-06', [{ start: '12:00', end: '23:59' }]),
      wt('2026-03-07', [{ start: '00:00', end: '12:00' }]),
    ]
    const expected = [{ date: '2026-03-07', endsAt: '12:00', restSeconds: 0, minimumHours: 11 }]
    expect(analyzeRestPeriods(month, [], '2026-03-06', () => 11)).toEqual(expected)
    expect(analyzeRestPeriods(month, [], '2026-03-07', () => 11)).toEqual(expected)
  })

  it('the earlier day sees the rest it shortens', () => {
    const month = [
      wt('2026-03-10', [{ start: '22:00', end: '23:59' }]),
      wt('2026-03-11', [{ start: '06:00', end: '14:00' }]),
    ]
    const expected = [{ date: '2026-03-11', endsAt: '14:00', restSeconds: 28800, minimumHours: 11 }]
    expect(analyzeRestPeriods(month, [], '2026-03-10', () => 11)).toEqual(expected)
    expect(analyzeRestPeriods(month, [], '2026-03-11', () => 11)).toEqual(expected)
  })

  it('a day whose work lies outside the judged window does not show the finding', () => {
    const month = [
      wt('2026-03-02', [{ start: '08:00', end: '23:00' }]),
      wt('2026-03-03', [{ start: '06:00', end: '12:00' }]),
    ]
    expect(analyzeRestPeriods(month, [], '2026-03-03', () => 11)).toEqual([
      { date: '2026-03-03', endsAt: '12:00', restSeconds: 25200, minimumHours: 11 },
    ])
    // ...while the 2nd shows both: its own 9 h finding and the 3rd's, which its work shortens.
    expect(analyzeRestPeriods(month, [], '2026-03-02', () => 11)).toEqual([
      { date: '2026-03-02', endsAt: '23:00', restSeconds: 32400, minimumHours: 11 },
      { date: '2026-03-03', endsAt: '12:00', restSeconds: 25200, minimumHours: 11 },
    ])
  })

  it('the month cut: a night shift over the last midnight is judged as March sees it, and in full', () => {
    const month = [
      wt('2026-03-31', [
        { start: '08:00', end: '16:00' },
        { start: '22:00', end: '23:59' },
      ]),
    ]
    const boundary = [wt('2026-04-01', [{ start: '00:00', end: '06:00' }])]
    expect(analyzeRestPeriods(month, boundary, '2026-03-31', () => 11)).toEqual([
      { date: '2026-03-31', endsAt: '23:59', restSeconds: 28800, minimumHours: 11 },
      { date: '2026-04-01', endsAt: '06:00', restSeconds: 21600, minimumHours: 11 },
    ])
  })
})

describe('analyzeRestPeriods — Europe/Copenhagen elapsed time', () => {
  // Kind F. Red conditions: the screen's wall-clock minute line counts 10,5 h between 17:00 and
  // 03:30 where 9,5 elapsed hours (34 200 s) are expected; the shared table's `spring-forward-night-is-an-hour-shorter` is the full row.
  it('the night the clocks go forward is an hour shorter: 17:00 to 03:30 is 9,5 elapsed hours', () => {
    const month = [
      wt('2026-03-28', [{ start: '08:00', end: '17:00' }]),
      wt('2026-03-29', [{ start: '03:30', end: '12:00' }]),
    ]
    // 17:00 CET to 03:30 CEST is 9.5 elapsed hours (10.5 on the wall clock).
    expect(analyzeRestPeriods(month, [], '2026-03-29', () => 11)).toEqual([
      { date: '2026-03-29', endsAt: '12:00', restSeconds: 34200, minimumHours: 11 },
    ])
  })
})
