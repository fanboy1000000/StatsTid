// S142 / TASK-14201 — unit tests for the frontend Copenhagen business-date helper.
//
// WHAT THESE PROVE. Three wrong implementations are plausible here and a careless test passes
// against all of them: (a) reading the UTC day, (b) reading the BROWSER'S local day, (c) adding a
// hardcoded offset (+01:00 or +02:00) instead of converting through the real zone. The instants
// below are chosen to kill each one, and mirror the backend's `BoundaryInstants.cs` exactly so the
// two tiers reason about the same moments.
//
// EVERY EXPECTED VALUE IS A LITERAL. None is computed by calling `copenhagenToday` — a test that
// derives its expectation from the function under test proves only that the function agrees with
// itself, which is true of every one of the wrong implementations above.
//
// WHY THE TEST ZONE IS FORCED. Without this, the "browser-local" bug is invisible on a developer
// machine that is already on Danish time: browser-local and Copenhagen agree there, so the test
// passes against the very defect it exists to catch. (The host these were written on is on Danish
// time; CI runs UTC. Neither should get to decide whether a test can fail.) Forcing a zone BEHIND
// Copenhagen makes the two disagree on every host. Node re-reads the TZ environment variable on
// assignment (v16+, Windows included), and `afterAll` restores it so the change cannot leak into
// other files sharing this worker process.
import { describe, it, expect, beforeAll, afterAll } from 'vitest'
import { copenhagenToday, COPENHAGEN_TIME_ZONE } from '../copenhagenDate'
import { forceTestTimeZone, restoreTestTimeZone } from './testTimeZone'

let restore: string | undefined

beforeAll(() => {
  // Deliberately neither Copenhagen nor UTC: in July this zone is UTC-04:00, so its local day, the
  // UTC day and the Copenhagen day are three distinguishable answers at the instants below.
  restore = forceTestTimeZone('America/New_York')
})

afterAll(() => {
  restoreTestTimeZone(restore)
})

describe('copenhagenToday', () => {
  // THE GUARD ON THE GUARD. Everything below is only discriminating while the runner really is in
  // a zone behind Copenhagen. If the forcing above ever stopped taking effect, these tests would
  // not fail — they would quietly start passing against a browser-local implementation, which is
  // the worst possible outcome. So assert the precondition with a LITERAL: at 22:30 UTC on 15 July
  // a host on America/New_York (EDT, UTC-04:00) reads 18:30 on the 15th.
  it('runs in a zone behind Copenhagen, which is what lets the tests below fail', () => {
    expect(new Date('2026-07-15T22:30:00Z').getDate()).toBe(15)
    expect(new Date('2026-07-15T22:30:00Z').getHours()).toBe(18)
  })

  it('names the zone it measures against', () => {
    expect(COPENHAGEN_TIME_ZONE).toBe('Europe/Copenhagen')
  })

  // Summer, 22:30 UTC. Copenhagen is CEST (UTC+02:00), so it is already 00:30 on the 16th there,
  // while the UTC day is still the 15th and New York's local day is the 15th (18:30 EDT).
  // KILLS: the raw-UTC implementation (answers the 15th), the browser-local implementation
  // (answers the 15th) AND a hardcoded +01:00 implementation (22:30 + 1h = 23:30, still the 15th).
  // This single instant is the strongest of the three.
  it('returns the Copenhagen day when a summer evening has already rolled over there', () => {
    expect(copenhagenToday(new Date('2026-07-15T22:30:00Z'))).toBe('2026-07-16')
  })

  // Winter, 23:30 UTC. Copenhagen is CET (UTC+01:00), so it is 00:30 on the 16th there.
  // KILLS: the raw-UTC implementation and the browser-local implementation (both answer the 15th).
  // DELIBERATELY SILENT about a hardcoded +01:00 implementation — that arithmetic happens to be
  // right in January. The summer case above is what rules that bug out; recorded here so nobody
  // later mistakes this assertion for stronger evidence than it is.
  it('returns the Copenhagen day when a winter evening has already rolled over there', () => {
    expect(copenhagenToday(new Date('2026-01-15T23:30:00Z'))).toBe('2026-01-16')
  })

  // Winter, 22:30 UTC — one hour earlier than the case above, and Copenhagen is still on the 15th
  // (23:30 CET). KILLS a hardcoded +02:00 implementation, which would roll the day over an hour
  // too early and answer the 16th. A correct implementation, a raw-UTC one and a +01:00 one all
  // agree here; this instant exists solely to catch the +02:00 mistake the other two cannot see.
  it('does NOT roll the day over an hour early in winter', () => {
    expect(copenhagenToday(new Date('2026-01-15T22:30:00Z'))).toBe('2026-01-15')
  })

  // Denmark is AHEAD of UTC year-round, so the Copenhagen day is never behind the UTC day. This
  // pins the ordinary midday case where all three calendars agree, so a future refactor that
  // broke the common path (rather than only the midnight edge) would be caught too.
  it('agrees with the UTC day at midday, when nothing is near a boundary', () => {
    expect(copenhagenToday(new Date('2026-07-15T12:00:00Z'))).toBe('2026-07-15')
    expect(copenhagenToday(new Date('2026-01-15T12:00:00Z'))).toBe('2026-01-15')
  })

  it('pads month and day to two digits so the result is always sortable yyyy-MM-dd', () => {
    expect(copenhagenToday(new Date('2026-03-05T12:00:00Z'))).toBe('2026-03-05')
  })

  it('defaults to the current wall clock when no instant is supplied', () => {
    // Shape assertion only — the VALUE cannot be a literal here without pinning the clock, and
    // deriving one from a second call would be circular. The pinned instants above carry the
    // correctness claim; this only proves the default parameter is wired.
    expect(copenhagenToday()).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})

// ── S142 Step-7a WARNING 1 — the ruling and the code had disagreed ────────────────────────────
//
// Owner ruling OQ-12 says an unresolvable Europe/Copenhagen must DISABLE the affected control with
// a message, not blank the page. Every UI guard implementing that is a try/catch around
// `copenhagenToday()`.
//
// Those guards were dead code. The formatter used to be constructed at MODULE SCOPE, and
// `new Intl.DateTimeFormat({ timeZone })` throws RangeError (ECMA-402) for a zone the runtime
// cannot resolve — so the throw happened during module EVALUATION, before any importing component
// existed to catch it. The user would have got the blank screen the ruling exists to prevent, one
// layer earlier than anyone was looking.
//
// The OQ-12 component tests could not see this: they `vi.doMock` the FUNCTION to throw when called,
// which is a failure the real module could not produce. These two facts exercise the real module
// with a real failing `Intl`, and are the only place that distinction is observable.
describe('copenhagenDate — an unresolvable zone fails at CALL time, not at import (OQ-12)', () => {
  const RealDateTimeFormat = Intl.DateTimeFormat

  afterEach(() => {
    ;(Intl as { DateTimeFormat: typeof Intl.DateTimeFormat }).DateTimeFormat = RealDateTimeFormat
    vi.resetModules()
  })

  function breakTheZone() {
    ;(Intl as { DateTimeFormat: unknown }).DateTimeFormat = function () {
      throw new RangeError('Invalid time zone specified: Europe/Copenhagen')
    } as unknown as typeof Intl.DateTimeFormat
  }

  it('IMPORTS cleanly even when the zone cannot be resolved', async () => {
    vi.resetModules()
    breakTheZone()
    // The assertion is that this resolves at all. With the module-scope formatter it rejected here,
    // and no component guard downstream could ever have run.
    const mod = await import('../copenhagenDate')
    expect(typeof mod.copenhagenToday).toBe('function')
  })

  it('throws from copenhagenToday(), where the UI guards can catch it', async () => {
    vi.resetModules()
    breakTheZone()
    const mod = await import('../copenhagenDate')
    expect(() => mod.copenhagenToday(new Date('2026-07-15T22:30:00Z'))).toThrow(RangeError)
  })
})
