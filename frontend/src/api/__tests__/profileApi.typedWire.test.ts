// S119 / TASK-11901 — WIRE-level pins for the typed profile switch (PAT-012).
// These exercise the REAL typed apiFetchWithEtag path (stubbed fetch, no hook
// mock), pinning the byte-equivalence the switch promised:
//
//  • BOTH flexible-precondition branches, byte-identical to the legacy
//    `headers:` composition — null etag → `If-None-Match: *` (the program's
//    FIRST live `ifNoneMatch` use) and non-null etag → `If-Match: "<version>"`
//    (with the raw-passthrough fallback for an unparseable legacy etag);
//  • exactly ONE precondition header per request (mutual exclusion);
//  • URLs (typed path-key interpolation) byte-identical to the legacy
//    encodeURIComponent template strings;
//  • the PUT body byte-identical (the 6-key ProfileSaveRequest);
//  • the 412-body ROUND-TRIP: a structured 412 response's body reaches the
//    caller's runtime narrowing (error shapes stay DELIBERATELY untyped —
//    guard, never cast).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getCurrentProfile, getProfileHistory, saveProfile } from '../profileApi'
import type { ProfileSaveRequest } from '../profileApi'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

type Captured = {
  url: string
  method: string
  body: unknown
  headers: Record<string, string>
}

function toHeaderRecord(headers: HeadersInit | undefined): Record<string, string> {
  const record: Record<string, string> = {}
  if (!headers) return record
  if (headers instanceof Headers) {
    headers.forEach((v, k) => { record[k] = v })
  } else if (Array.isArray(headers)) {
    for (const [k, v] of headers) record[k] = v
  } else {
    for (const [k, v] of Object.entries(headers)) record[k] = v
  }
  return record
}

function captureCalls(
  respond: () => { status?: number; body?: unknown; etag?: string } = () => ({}),
) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      headers: toHeaderRecord(init?.headers),
    })
    const { status = 200, body = {}, etag } = respond()
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: new Headers(etag ? { ETag: etag } : {}),
      json: async () => body,
      text: async () => JSON.stringify(body),
    }
  })
  return calls
}

beforeEach(() => {
  mockFetch.mockReset()
})

// A full 14-member spec profile (LocalAgreementProfileResponse).
const profile = {
  profileId: '11111111-2222-3333-4444-555555555555',
  orgId: 'STY02',
  agreementCode: 'HK',
  okVersion: 'OK24',
  effectiveFrom: '2026-07-20',
  effectiveTo: null,
  weeklyNormHours: 37,
  maxFlexBalance: null,
  flexCarryoverMax: null,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
  createdBy: 'admin1',
  createdAt: '2026-07-20T12:00:00Z',
  version: 5,
}

const saveBody: ProfileSaveRequest = {
  effectiveFrom: '2026-07-20',
  weeklyNormHours: 37,
  maxFlexBalance: null,
  flexCarryoverMax: null,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
}

describe('profile typed reads — the exact legacy URLs, no precondition', () => {
  it('getCurrentProfile → GET the interpolated profile URL', async () => {
    const calls = captureCalls(() => ({ body: profile, etag: '"5"' }))
    const result = await getCurrentProfile('STY02', 'HK', 'OK24')
    expect(calls[0].url).toBe('/api/config/STY02/profile/HK/OK24')
    expect(calls[0].method).toBe('GET')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.profile?.version).toBe(5)
    expect(result.etag).toBe('"5"')
  })

  it('getProfileHistory → GET …/history; no ETag expected (immutable rows)', async () => {
    const calls = captureCalls(() => ({ body: [profile] }))
    const result = await getProfileHistory('STY02', 'HK', 'OK24')
    expect(calls[0].url).toBe('/api/config/STY02/profile/HK/OK24/history')
    expect(calls[0].method).toBe('GET')
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.history).toHaveLength(1)
  })

  it('path segments are still URI-encoded exactly like the legacy template', async () => {
    const calls = captureCalls(() => ({ body: profile }))
    await getCurrentProfile('STY 02', 'A/C', 'OK24')
    expect(calls[0].url).toBe('/api/config/STY%2002/profile/A%2FC/OK24')
  })
})

describe('saveProfile — BOTH precondition branches, byte-identical', () => {
  it('null etag → If-None-Match: * (create-only; the FIRST live ifNoneMatch use) and NO If-Match', async () => {
    const calls = captureCalls(() => ({ body: { ...profile, version: 1 }, etag: '"1"' }))
    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, null)
    expect(calls[0].url).toBe('/api/config/STY02/profile/HK/OK24')
    expect(calls[0].method).toBe('PUT')
    expect(calls[0].headers['If-None-Match']).toBe('*')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(result.ok).toBe(true)
  })

  it('non-null etag → If-Match: "<version>" (canonical re-format) and NO If-None-Match', async () => {
    const calls = captureCalls(() => ({ body: { ...profile, version: 6 }, etag: '"6"' }))
    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')
    expect(calls[0].method).toBe('PUT')
    expect(calls[0].headers['If-Match']).toBe('"5"')
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.newEtag).toBe('"6"')
    expect(result.newVersion).toBe(6)
  })

  it('unquoted etag is re-formatted to the canonical quoted wire form', async () => {
    const calls = captureCalls(() => ({ body: profile, etag: '"6"' }))
    await saveProfile('STY02', 'HK', 'OK24', saveBody, '5')
    expect(calls[0].headers['If-Match']).toBe('"5"')
  })

  it('an UNPARSEABLE legacy etag passes through RAW (the profileApi fallback, preserved)', async () => {
    const legacyUuidEtag = '"11111111-2222-3333-4444-555555555555"'
    const calls = captureCalls(() => ({ body: profile, etag: '"6"' }))
    await saveProfile('STY02', 'HK', 'OK24', saveBody, legacyUuidEtag)
    expect(calls[0].headers['If-Match']).toBe(legacyUuidEtag)
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
  })

  it('the PUT body is byte-identical: exactly the 6 ProfileSaveRequest keys', async () => {
    const calls = captureCalls(() => ({ body: profile, etag: '"6"' }))
    await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')
    expect(calls[0].body).toEqual(saveBody)
    expect(Object.keys(calls[0].body as Record<string, unknown>).sort()).toEqual([
      'effectiveFrom',
      'flexCarryoverMax',
      'maxFlexBalance',
      'maxOvertimeHoursPerPeriod',
      'overtimeRequiresPreApproval',
      'weeklyNormHours',
    ])
  })
})

describe('saveProfile — the 412-body round-trip (structured error narrowing preserved)', () => {
  it('a structured 412 body reaches the caller: expected/actual version + currentState', async () => {
    const staleBody = {
      error: 'Stale profile version',
      expectedVersion: 5,
      actualVersion: 7,
      currentState: { ...profile, version: 7 },
    }
    captureCalls(() => ({ status: 412, body: staleBody }))
    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.status).toBe(412)
    // The runtime-guard narrowing (never a cast): the structured members are
    // reachable for the caller's banner rendering.
    expect(result.body?.expectedVersion).toBe(5)
    expect(result.body?.actualVersion).toBe(7)
    expect(result.body?.currentState).toEqual({ ...profile, version: 7 })
  })

  it('a structured 400 body reaches the caller: per-field errors', async () => {
    const fieldBody = {
      error: 'Validation failed',
      fields: [{ field: 'weeklyNormHours', code: 'OUT_OF_RANGE', message: 'ugyldig' }],
    }
    captureCalls(() => ({ status: 400, body: fieldBody }))
    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, null)
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.status).toBe(400)
    expect(result.body?.fields?.[0]?.field).toBe('weeklyNormHours')
  })

  it('a NON-object error body narrows to undefined (guard, not cast)', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 500,
      headers: new Headers(),
      json: async () => 'plain text',
      text: async () => 'plain text',
    })
    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.body).toBeUndefined()
  })
})
