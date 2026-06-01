// S59 follow-up (ADR-029, Step-7a BLOCKER 1) — read-then-If-Match contract for
// the HR-only CHILD_SICK eligibility hook.
//
// The backend write is now strictly precondition-guarded:
//   • GET …/entitlement-eligibility/CHILD_SICK
//       - live row → 200 { eligible, rowExists:true, version } + ETag "<version>"
//       - no row   → 200 { eligible:false, rowExists:false } with NO ETag
//   • PUT  create (no row)  → If-None-Match: *
//     PUT  update (row)     → If-Match: "<version>"
//     PUT  If-None-Match: * against an existing row → 409 (lost update).
//
// `useEntitlementEligibility` exposes no React state (it just returns closures),
// so we exercise the returned functions directly against a stubbed fetch.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useEntitlementEligibility } from '../useEntitlementEligibility'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

function hook() {
  return renderHook(() => useEntitlementEligibility()).result.current
}

beforeEach(() => {
  mockFetch.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

const ELIG_URL = '/api/admin/employees/EMP001/entitlement-eligibility/CHILD_SICK'

describe('useEntitlementEligibility — fetchChildSickEligibility (read)', () => {
  it('maps a live row → rowExists:true + version (from ETag)', async () => {
    mockFetch.mockResolvedValue({
      ok: true, status: 200,
      headers: new Headers({ ETag: '"4"' }),
      json: async () => ({
        employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
        eligible: true, effectiveFrom: '2026-05-01', rowExists: true, version: 4,
      }),
    })
    const snap = await hook().fetchChildSickEligibility('EMP001')
    expect(snap).toEqual({ employeeId: 'EMP001', eligible: true, rowExists: true, version: 4 })
    expect(mockFetch.mock.calls[0][0]).toBe(ELIG_URL)
  })

  it('maps an absent row → rowExists:false + version null (no ETag)', async () => {
    mockFetch.mockResolvedValue({
      ok: true, status: 200,
      headers: new Headers(),
      json: async () => ({
        employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
        eligible: false, rowExists: false,
      }),
    })
    const snap = await hook().fetchChildSickEligibility('EMP001')
    expect(snap).toEqual({ employeeId: 'EMP001', eligible: false, rowExists: false, version: null })
  })
})

describe('useEntitlementEligibility — setChildSick (write precondition)', () => {
  function capturePutAndRespond(respond: () => Response) {
    const calls: Array<{ headers: Record<string, string>; body: unknown }> = []
    mockFetch.mockImplementation(async (_url: string, init?: RequestInit) => {
      const headers: Record<string, string> = {}
      const h = init?.headers as Record<string, string> | undefined
      if (h) for (const [k, v] of Object.entries(h)) headers[k] = v
      calls.push({ headers, body: init?.body ? JSON.parse(init.body as string) : undefined })
      return respond()
    })
    return calls
  }

  it('CREATE (no live row) sends If-None-Match: * and no If-Match', async () => {
    const calls = capturePutAndRespond(() => ({
      ok: true, status: 200,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => ({
        employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
        eligible: true, effectiveFrom: '2026-06-01', version: 1,
      }),
    }) as unknown as Response)

    const snap = await hook().setChildSick('EMP001', true, /*rowExists*/ false, /*version*/ null)
    expect(calls[0].headers['If-None-Match']).toBe('*')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(calls[0].body).toEqual({ eligible: true })
    expect(snap).toEqual({ employeeId: 'EMP001', eligible: true, rowExists: true, version: 1 })
  })

  it('UPDATE (live row) sends If-Match: "<version>" and no If-None-Match', async () => {
    const calls = capturePutAndRespond(() => ({
      ok: true, status: 200,
      headers: new Headers({ ETag: '"5"' }),
      json: async () => ({
        employeeId: 'EMP001', entitlementType: 'CHILD_SICK',
        eligible: false, effectiveFrom: '2026-06-01', version: 5,
      }),
    }) as unknown as Response)

    const snap = await hook().setChildSick('EMP001', false, /*rowExists*/ true, /*version*/ 4)
    expect(calls[0].headers['If-Match']).toBe('"4"')
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
    expect(snap).toEqual({ employeeId: 'EMP001', eligible: false, rowExists: true, version: 5 })
  })

  it('surfaces a clear lost-update message + body on 409', async () => {
    mockFetch.mockResolvedValue({
      ok: false, status: 409,
      headers: new Headers(),
      text: async () => JSON.stringify({
        error: 'Eligibility row already exists',
        currentVersion: 9,
        hint: 'If-None-Match: * is create-only. Re-read (GET) and retry with If-Match.',
      }),
    })
    await expect(
      hook().setChildSick('EMP001', true, false, null),
    ).rejects.toMatchObject({
      status: 409,
      message: expect.stringMatching(/ændret af en anden administrator/i),
      body: { currentVersion: 9 },
    })
  })
})
