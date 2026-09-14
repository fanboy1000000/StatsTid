// SPRINT-141 / TASK-14107 — the Step-0b BLOCKER regression pin (OQ-3 (a)'s
// THIRD specification; the first two were wrong — see SPRINT-141.md for the
// full history).
//
// THE DEFECT THIS GUARDS: the edit drawer saves in a sequence — users PUT
// (step 1, unconditional, bumps `users.version` even when nothing changed) →
// employee-profiles PUT (step 2) → DOB PUT (step 3) → employment-start PUT
// (step 4). S141 moved the profile row's concurrency token from its own
// `ep.version` to `users.version` (OQ-3 (a), the ONE aggregate token for the
// whole employee record). If the profile PUT sends the STALE pre-open
// `ep.version`/etag instead of the POST-STEP-1 `users.version`, it 412s —
// forever, on every save, for every employee. And if it re-stamps only the
// profile snapshot on success (the natural-looking fix) rather than ALSO
// `live.user.version`/`etag`, the 412 simply migrates to the NEXT write in the
// sequence (the DOB PUT here) instead of disappearing.
//
// THE TEST'S DISCRIMINATING SHAPE, following the task's own warning: a pin
// that dirties ONLY the profile field would pass even with the defect fully
// present (the DOB write never fires, so nothing downstream ever notices the
// stale token). This test dirties the PROFILE **and** the DATE OF BIRTH
// together, and its server double REJECTS a stale If-Match at both the
// profile PUT and the DOB PUT — exactly the shape the sprint log says the
// project already learned once ("the mock that accepted every PUT masked
// this") and had to re-learn for the profile write.
//
// `live.profile`'s OWN version/etag is seeded to a number (42) that is
// DELIBERATELY different from `live.user`'s (5) — the row's own pre-S141
// token, now orphaned. If the fix regresses to using it, the profile PUT's
// If-Match ("42") will not match what the double expects ("6") and the whole
// save reports `ok: false`.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { useEditPerson, type EditSaveInput } from '../useEditPerson'
import type { EditLiveState, SaveEditResult } from '../useEditPerson'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 't' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => {
    mockStorage[k] = v
  },
  removeItem: (k: string) => {
    delete mockStorage[k]
  },
})

interface Recorded {
  url: string
  method: string
  body: Record<string, unknown> | null
  ifMatch: string | null
}
let calls: Recorded[]

function res(ok: boolean, status: number, json: unknown, etag?: string) {
  return {
    ok,
    status,
    headers: new Headers(etag ? { ETag: etag } : {}),
    json: async () => json,
    text: async () => JSON.stringify(json),
  } as unknown as Response
}

/** A server double that REJECTS a stale `If-Match` — the shape the task
    warns a permissive mock (S76b's original "accepted every PUT") would
    have hidden. Each dated write's expected precondition is supplied by the
    test so the same double serves every scenario below. */
function setupRouter(routes: Record<string, { expectIfMatch?: string; ok: (rec: Recorded) => Response }>) {
  calls = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const headers = (init?.headers ?? {}) as Record<string, string>
    const rec: Recorded = {
      url,
      method: init?.method ?? 'GET',
      body: init?.body ? JSON.parse(init.body as string) : null,
      ifMatch: headers['If-Match'] ?? null,
    }
    calls.push(rec)
    for (const [pattern, route] of Object.entries(routes)) {
      if (!url.includes(pattern)) continue
      if (route.expectIfMatch !== undefined && rec.ifMatch !== route.expectIfMatch) {
        // A stale (or wrong-source) token — the real backend's 412.
        return res(false, 412, {
          error: 'stale version',
          expectedVersion: Number(route.expectIfMatch?.replace(/"/g, '')),
          actualVersion: rec.ifMatch ? Number(rec.ifMatch.replace(/"/g, '')) : undefined,
        })
      }
      return route.ok(rec)
    }
    throw new Error(`no route for ${rec.method} ${url}`)
  })
}

function makeLive(): EditLiveState {
  return {
    user: {
      userId: 'EMP1',
      username: 'emp1',
      displayName: 'Test Bruger',
      email: 'e@x.dk',
      primaryOrgId: 'STY02',
      agreementCode: 'AC',
      version: 5,
      etag: '"5"',
    },
    // The profile row's OWN (pre-S141) token — deliberately DIFFERENT from
    // `live.user`'s, so a regression to the old "use the profile's own etag"
    // behaviour is caught rather than accidentally matching by coincidence.
    profile: {
      employeeId: 'EMP1',
      partTimeFraction: 1.0,
      position: 'Old Title',
      isPartTime: false,
      version: 42,
      etag: '"42"',
      scheduled: null,
    },
    birthDateVersion: 5,
    birthDateInitial: '',
    employmentStartVersion: 5,
    employmentStartInitial: '',
    childSickRowExists: false,
    childSickVersion: null,
  }
}

function dirtyInput(): EditSaveInput {
  return {
    stamdata: { displayName: 'Test Bruger', email: 'e@x.dk', primaryOrgId: 'STY02', agreementCode: 'AC' },
    // DIRTIED vs live.profile ('1.000' / 'Old Title').
    profile: { partTimeFraction: '0.800', position: 'New Title' },
    // DIRTIED vs live.birthDateInitial (''); employmentStartDate left AT its
    // initial ('') so step (4) does not fire — the test stays focused on the
    // profile + DOB pair the task names.
    entitlement: { birthDate: '1985-05-01', employmentStartDate: '', childSickEligible: false },
    childSickDirty: false,
    isHr: true,
  }
}

beforeEach(() => {
  mockFetch.mockReset()
})

function Harness({
  input,
  live,
  onResult,
}: {
  input: EditSaveInput
  live: EditLiveState
  onResult: (r: SaveEditResult) => void
}) {
  const { saveEdit } = useEditPerson()
  return <button onClick={async () => onResult(await saveEdit(input, live))}>save</button>
}

async function run(input: EditSaveInput, live: EditLiveState): Promise<SaveEditResult> {
  let captured: SaveEditResult | null = null
  render(<Harness input={input} live={live} onResult={(r) => (captured = r)} />)
  fireEvent.click(screen.getByText('save'))
  await waitFor(() => expect(captured).not.toBeNull())
  return captured as unknown as SaveEditResult
}

describe('useEditPerson.saveEdit — the running users.version cursor (S141 / OQ-3(a) BLOCKER)', () => {
  it('threads the POST-STEP-1 token into the profile PUT, and the POST-PROFILE token into the DOB PUT — dirtying profile AND date of birth together', async () => {
    setupRouter({
      // Step 1 — users PUT. Unconditional; bumps users.version 5 -> 6.
      '/api/admin/users/EMP1': {
        expectIfMatch: '"5"',
        ok: () =>
          res(
            true,
            200,
            {
              userId: 'EMP1',
              displayName: 'Test Bruger',
              email: 'e@x.dk',
              primaryOrgId: 'STY02',
              agreementCode: 'AC',
              version: 6,
            },
            '"6"',
          ),
      },
      // Step 2 — employee-profiles PUT. MUST receive the POST-STEP-1 token
      // ("6"), NOT the profile row's own pre-open etag ("42"). Bumps
      // users.version 6 -> 7 (S141: the token is `users.version`, and every
      // write bumps it — see EmployeeProfileResponse.version's doc).
      '/api/admin/employee-profiles/EMP1': {
        expectIfMatch: '"6"',
        ok: () =>
          res(
            true,
            200,
            {
              employeeId: 'EMP1',
              partTimeFraction: 0.8,
              position: 'New Title',
              isPartTime: true,
              version: 7,
              scheduled: null,
            },
            '"7"',
          ),
      },
      // Step 3 — DOB PUT. MUST receive the POST-PROFILE token ("7"). If the
      // profile step failed to re-stamp `live.user`, this would still see
      // "6" (or the DEFECT's "5") and 412 — the failure would show up HERE,
      // one step downstream of where it was introduced.
      '/api/admin/employees/EMP1/birth-date': {
        expectIfMatch: '"7"',
        ok: () => res(true, 200, { employeeId: 'EMP1', birthDate: '1985-05-01', version: 8 }, '"8"'),
      },
    })

    const result = await run(dirtyInput(), makeLive())

    expect(result.ok).toBe(true)
    expect(result.staleConflict).toBeNull()
    expect(result.error).toBeNull()

    // The running cursor ended at 8 (users -> 6 -> profile -> 7 -> DOB -> 8):
    // every write in the sequence — AND anything reading `live.user.version`
    // afterwards (usePlacement's post-save unit-assign) — sees the REAL final
    // token, not a superseded one.
    expect(result.live.user.version).toBe(8)
    expect(result.live.user.etag).toBe('"8"')
    // The profile snapshot itself is ALSO re-stamped (read-your-write for a
    // same-session reopen), not just the user side.
    expect(result.live.profile?.version).toBe(7)
    expect(result.live.profile?.etag).toBe('"7"')

    // Confirm the exact tokens each write actually sent (belt-and-braces: the
    // router's expectIfMatch already enforces this, but asserting it directly
    // makes the regression's shape legible in the test output, not just a
    // pass/fail).
    const profileCall = calls.find((c) => c.url.includes('/employee-profiles/'))!
    expect(profileCall.ifMatch).toBe('"6"')
    const dobCall = calls.find((c) => c.url.includes('/birth-date'))!
    expect(dobCall.ifMatch).toBe('"7"')
  })

  it('a profile-PUT 412 (a genuinely stale token) is reported honestly rather than silently swallowed', async () => {
    // The router's OWN discriminating guard (expectIfMatch) doubles as the
    // "real backend rejects a stale token" case: seed a live.user version the
    // users PUT does NOT expect, so step 1 itself 412s and the whole save
    // short-circuits with the banner-with-retry shape (`staleConflict`
    // populated) — the drawer must not report success while the server
    // refused the write.
    setupRouter({
      '/api/admin/users/EMP1': {
        expectIfMatch: '"999"', // the double expects a version the caller never has
        ok: () => res(true, 200, {}, '"1000"'),
      },
    })

    const result = await run(dirtyInput(), makeLive())

    expect(result.ok).toBe(false)
    expect(result.staleConflict).not.toBeNull()
    // The short-circuit means the profile/DOB PUTs — which would need a
    // users.version this save never obtained — are never attempted.
    expect(calls).toHaveLength(1)
  })
})
