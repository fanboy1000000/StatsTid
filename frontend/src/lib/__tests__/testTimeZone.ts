// S142 / TASK-14201 — test-only helper for running a spec in a chosen IANA time zone.
//
// WHY IT EXISTS. Any test that has to tell "the Danish calendar day" apart from "the day where
// this machine is sitting" is worthless unless the machine is demonstrably NOT on Danish time.
// The developer host this sprint was written on is on Danish time and CI runs UTC, so a test that
// relied on either would be discriminating in one place and vacuous in the other. Forcing the zone
// makes the distinction real on every host.
//
// HOW. Node re-reads the `TZ` environment variable whenever it is assigned (v16+, Windows
// included), and jsdom's `Date` is Node's `Date`, so assigning it changes what the browser-local
// `getFullYear()/getMonth()/getDate()` family answers. `process` is reached through `globalThis`
// rather than a bare global because this project's tsconfig deliberately loads only
// `vitest/globals` types, not Node's — a test helper should not be the reason browser code gains
// access to Node globals it must never use at runtime.
//
// ALWAYS PAIR THE TWO CALLS. `process.env` is shared by every spec file in the same worker
// process, so a forced zone that is never restored leaks into whatever file runs next.

interface NodeProcessLike {
  env: Record<string, string | undefined>
}

function nodeProcess(): NodeProcessLike {
  const candidate = (globalThis as { process?: NodeProcessLike }).process
  if (!candidate || typeof candidate.env !== 'object') {
    // Fail loudly. Silently skipping the override would leave the calling test running in the
    // host's own zone, where it can pass against the very bug it was written to catch.
    throw new Error(
      'Cannot force a test time zone: no Node `process.env` is reachable from this runner. ' +
        'The calling test cannot discriminate a browser-local date bug without it.',
    )
  }
  return candidate
}

/**
 * Sets the process time zone and returns the previous value, which must be handed back to
 * {@link restoreTestTimeZone} in an `afterAll`.
 *
 * @param timeZone An IANA zone id, e.g. `'America/New_York'`.
 * @returns The previous `TZ` value (`undefined` if it was not set).
 */
export function forceTestTimeZone(timeZone: string): string | undefined {
  const proc = nodeProcess()
  const previous = proc.env.TZ
  proc.env.TZ = timeZone
  return previous
}

/** Restores the value {@link forceTestTimeZone} returned. */
export function restoreTestTimeZone(previous: string | undefined): void {
  const proc = nodeProcess()
  if (previous === undefined) delete proc.env.TZ
  else proc.env.TZ = previous
}
