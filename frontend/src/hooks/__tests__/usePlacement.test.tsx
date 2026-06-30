// SPRINT-109 / TASK-10902 — the keystone: the 4-case PLACEMENT routing. Drives the
// REAL usePlacement (→ useEditPerson + useUnitMutations) through a URL/method fetch
// router that RECORDS every call in order, so the assertions pin the actual HTTP
// sequence + the If-Match version threading — RED on a mis-route (a /unit call on a
// cross-Org transfer), RED on the 412-every-time double-write (the pre-read version
// threaded), and RED on the leadership-wipe (designate before the unit-assign).
//
// isHr=false + unchanged HR fields ⇒ saveEdit fires ONLY the stamdata PUT, isolating
// the routing from the HR sub-writes (those have their own S76b coverage).

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { usePlacement, type PlacementArgs, type PlacementResult } from '../usePlacement'
import type { EditLiveState } from '../useEditPerson'

// ── fetch + localStorage stubs ──────────────────────────────────────────────────
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

function res(ok: boolean, status: number, json: unknown, etag?: string): Response {
  return {
    ok,
    status,
    headers: new Headers(etag ? { ETag: etag } : {}),
    json: async () => json,
    text: async () => JSON.stringify(json),
  } as unknown as Response
}

type Route = (rec: Recorded) => Response
function setupRouter(routes: Record<string, Route>) {
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
    // Most specific patterns first (so /users/{id}/unit beats /users/{id}).
    for (const [pattern, route] of Object.entries(routes)) {
      if (url.includes(pattern)) return route(rec)
    }
    throw new Error(`no route for ${rec.method} ${url}`)
  })
}

function Harness({ args, onResult }: { args: PlacementArgs; onResult: (r: PlacementResult) => void }) {
  const { savePlacement } = usePlacement()
  return (
    <button onClick={async () => onResult(await savePlacement(args))}>save</button>
  )
}

function makeLive(version: number): EditLiveState {
  return {
    user: {
      userId: 'EMP1',
      username: 'emp1',
      displayName: 'Test Bruger',
      email: 'e@x.dk',
      primaryOrgId: 'STY02',
      agreementCode: 'AC',
      version,
      etag: `"${version}"`,
    },
    profile: null,
    birthDateVersion: null,
    birthDateInitial: '',
    employmentStartVersion: null,
    employmentStartInitial: '',
    childSickRowExists: false,
    childSickVersion: null,
  }
}

function editInput(primaryOrgId: string) {
  return {
    stamdata: { displayName: 'Test Bruger', email: 'e@x.dk', primaryOrgId, agreementCode: 'AC' },
    profile: { partTimeFraction: '1.000', position: '' },
    entitlement: { birthDate: '', employmentStartDate: '', childSickEligible: false },
    childSickDirty: false,
    isHr: false,
  }
}

const userJson = (version: number) => ({
  userId: 'EMP1',
  username: 'emp1',
  displayName: 'Test Bruger',
  email: 'e@x.dk',
  primaryOrgId: 'STY02',
  agreementCode: 'AC',
  version,
})

beforeEach(() => {
  mockFetch.mockReset()
})

async function run(args: PlacementArgs): Promise<PlacementResult> {
  let captured: PlacementResult | null = null
  render(<Harness args={args} onResult={(r) => (captured = r)} />)
  fireEvent.click(screen.getByText('save'))
  await waitFor(() => expect(captured).not.toBeNull())
  return captured as unknown as PlacementResult
}

describe('usePlacement — the 4-case PLACEMENT routing (TASK-10902)', () => {
  // ── CASE 1 — create + Placering → POST then /unit (v=1) then promote ───────────
  it('CREATE + Placering: POST /users, THEN PUT /users/{id}/unit with the create version (v=1), THEN designate', async () => {
    setupRouter({
      '/api/admin/users/NEW1/unit': () => res(true, 200, { version: 2 }, '"2"'),
      '/api/admin/units/unit-A/leaders': () => res(true, 200, {}),
      '/api/admin/users': () => res(true, 201, { ...userJson(1), userId: 'NEW1' }, '"1"'),
    })
    const result = await run({
      mode: 'create',
      createBody: {
        userId: 'NEW1', username: 'new1', password: 'pw', displayName: 'Ny Person',
        primaryOrgId: 'STY02', agreementCode: 'AC', okVersion: 'OK24',
      },
      targetUnitId: 'unit-A',
      designateUnitId: 'unit-A',
    })

    expect(result.ok).toBe(true)
    const seq = calls.map((c) => `${c.method} ${c.url.replace('/api/admin', '')}`)
    expect(seq).toEqual(['POST /users', 'PUT /users/NEW1/unit', 'POST /units/unit-A/leaders'])
    // the unit-assign carried the create's returned version (v=1).
    const unitCall = calls.find((c) => c.url.endsWith('/unit'))!
    expect(unitCall.ifMatch).toBe('"1"')
    expect(unitCall.body).toEqual({ unitId: 'unit-A' })
  })

  it('CREATE without a Placering: POST only (no /unit)', async () => {
    setupRouter({ '/api/admin/users': () => res(true, 201, { ...userJson(1), userId: 'NEW1' }, '"1"') })
    const result = await run({
      mode: 'create',
      createBody: {
        userId: 'NEW1', username: 'new1', password: 'pw', displayName: 'Ny',
        primaryOrgId: 'STY02', agreementCode: 'AC', okVersion: 'OK24',
      },
      targetUnitId: null,
      designateUnitId: null,
    })
    expect(result.ok).toBe(true)
    expect(calls.filter((c) => c.url.includes('/unit'))).toHaveLength(0)
  })

  // ── CASE 2 — Org changed → ONE /users/{id} with primaryOrgId + unitId ──────────
  it('EDIT, Org CHANGED: ONE PUT /users/{id} carrying primaryOrgId AND unitId; NEVER a /unit call', async () => {
    setupRouter({
      '/api/admin/users/EMP1/unit': () => res(true, 200, { version: 99 }, '"99"'),
      '/api/admin/users/EMP1': () => res(true, 200, { ...userJson(6), primaryOrgId: 'STY03' }, '"6"'),
    })
    const result = await run({
      mode: 'edit',
      userId: 'EMP1',
      editInput: editInput('STY03'),
      live: makeLive(5),
      orgChanged: true,
      targetUnitId: 'unit-B',
      unitChanged: true,
      designateUnitId: null,
      removeLeaderUnitId: null,
    })

    expect(result.ok).toBe(true)
    // the transfer is ONE call to /users/{id} (NOT /users/{id}/unit).
    expect(calls.filter((c) => c.url.endsWith('/unit'))).toHaveLength(0)
    const transfer = calls.find((c) => c.method === 'PUT' && c.url.endsWith('/users/EMP1'))!
    expect(transfer.body).toMatchObject({ primaryOrgId: 'STY03', unitId: 'unit-B' })
  })

  it('EDIT, Org CHANGED, manager-with-active-reports: surfaces the 422 block; NO /unit call', async () => {
    setupRouter({
      '/api/admin/users/EMP1': () =>
        res(false, 422, { error: 'Cannot transfer a user who still manages active reports; re-assign their reports first.', activeReportCount: 2 }),
    })
    const result = await run({
      mode: 'edit',
      userId: 'EMP1',
      editInput: editInput('STY03'),
      live: makeLive(5),
      orgChanged: true,
      targetUnitId: null,
      unitChanged: false,
      designateUnitId: null,
      removeLeaderUnitId: null,
    })
    expect(result.ok).toBe(false)
    expect(result.error).toContain('Cannot transfer a user who still manages active reports')
    expect(calls.filter((c) => c.url.includes('/unit'))).toHaveLength(0)
  })

  // ── CASE 3 — same-Org Placering change → /unit with the FINAL threaded version ──
  it('EDIT, SAME-Org Placering change: stamdata PUT bumps the version → /unit threads the FINAL version (NOT the pre-read etag → never a 412)', async () => {
    setupRouter({
      '/api/admin/users/EMP1/unit': () => res(true, 200, { version: 7 }, '"7"'),
      '/api/admin/users/EMP1': () => res(true, 200, userJson(6), '"6"'),
    })
    const result = await run({
      mode: 'edit',
      userId: 'EMP1',
      editInput: editInput('STY02'), // SAME org
      live: makeLive(5), // pre-read version 5
      orgChanged: false,
      targetUnitId: 'unit-C',
      unitChanged: true,
      designateUnitId: null,
      removeLeaderUnitId: null,
    })

    expect(result.ok).toBe(true)
    const stamdata = calls.find((c) => c.method === 'PUT' && c.url.endsWith('/users/EMP1'))!
    const unitCall = calls.find((c) => c.url.endsWith('/unit'))!
    // the stamdata PUT used the pre-read etag (5); the /unit PUT MUST use the FINAL
    // version the stamdata PUT returned (6), NOT the stale pre-read 5.
    expect(stamdata.ifMatch).toBe('"5"')
    expect(unitCall.ifMatch).toBe('"6"')
    expect(result.version).toBe(7)
  })

  it('EDIT, SAME-Org, NO unit change: only the stamdata PUT (no /unit)', async () => {
    setupRouter({ '/api/admin/users/EMP1': () => res(true, 200, userJson(6), '"6"') })
    const result = await run({
      mode: 'edit',
      userId: 'EMP1',
      editInput: editInput('STY02'),
      live: makeLive(5),
      orgChanged: false,
      targetUnitId: null,
      unitChanged: false,
      designateUnitId: null,
      removeLeaderUnitId: null,
    })
    expect(result.ok).toBe(true)
    expect(calls.filter((c) => c.url.includes('/unit'))).toHaveLength(0)
  })

  // ── CASE 4 — move + promote → unit-assign BEFORE designate ─────────────────────
  it('EDIT, move + promote: runs the unit-assign FIRST, THEN designateLeader (the assign strips leaderships)', async () => {
    setupRouter({
      '/api/admin/users/EMP1/unit': () => res(true, 200, { version: 7 }, '"7"'),
      '/api/admin/units/unit-C/leaders': () => res(true, 200, {}),
      '/api/admin/users/EMP1': () => res(true, 200, userJson(6), '"6"'),
    })
    const result = await run({
      mode: 'edit',
      userId: 'EMP1',
      editInput: editInput('STY02'),
      live: makeLive(5),
      orgChanged: false,
      targetUnitId: 'unit-C',
      unitChanged: true,
      designateUnitId: 'unit-C',
      removeLeaderUnitId: null,
    })

    expect(result.ok).toBe(true)
    const unitIdx = calls.findIndex((c) => c.url.endsWith('/unit'))
    const leaderIdx = calls.findIndex((c) => c.url.endsWith('/leaders'))
    expect(unitIdx).toBeGreaterThanOrEqual(0)
    expect(leaderIdx).toBeGreaterThanOrEqual(0)
    // the unit-assign MUST precede the designate (else the move wipes the leadership).
    expect(unitIdx).toBeLessThan(leaderIdx)
  })
})
