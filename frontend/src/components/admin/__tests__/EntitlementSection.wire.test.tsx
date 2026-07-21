// S118 / TASK-11801 — WIRE-level pins for the child-entitlement sub-resource
// switch in EntitlementSection (PAT-012). Component-level (fetch stubbed):
//
//  • create POST → the TYPED interpolated URL, UNCONDITIONED (no If-Match /
//    If-None-Match), body byte-identical — and the W2 zero-payload-change pin:
//    `fullDayOnly` / `effectiveFrom` are NOT added to the create body either;
//  • update PUT (the SANCTIONED DEFERRED legacy call — the W2 ruling's named
//    deferred defect) → If-Match `"<version>"` kept; the CURRENT bytes pinned:
//    NO `fullDayOnly` (the pinned 422 dead-end for CARE_DAY/SENIOR_DAY) and
//    NO `effectiveFrom` (the binder-required member whose omission 400s every
//    child edit — the sweep's wider finding). Both stay ABSENT until a future
//    deliberate fix;
//  • delete DELETE → the TYPED form, If-Match kept, NO body, 204;
//  • the read-side additive pin: the spec `Entitlement` row REQUIRES
//    `fullDayOnly` (display/typing gain only this pass).
import { describe, it, expect, expectTypeOf, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { EntitlementSection, type Entitlement } from '../EntitlementSection'

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

function captureCalls(respond: (method: string) => { status?: number; body?: unknown } = () => ({})) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      headers: toHeaderRecord(init?.headers),
    })
    const { status = 200, body = {} } = respond(init?.method ?? 'GET')
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: new Headers(),
      json: async () => body,
      text: async () => JSON.stringify(body),
    }
  })
  return calls
}

const careDayRow: Entitlement = {
  configId: 'e-1',
  entitlementType: 'CARE_DAY',
  agreementCode: 'AC',
  okVersion: 'OK24',
  annualQuota: 2,
  accrualModel: 'IMMEDIATE',
  resetMonth: 1,
  carryoverMax: 0,
  proRateByPartTime: false,
  isPerEpisode: false,
  minAge: null,
  description: null,
  fullDayOnly: true,
  effectiveFrom: '2026-04-01',
  effectiveTo: null,
  version: 4,
}

const EXPECTED_BODY_KEYS = [
  'accrualModel', 'annualQuota', 'carryoverMax', 'description', 'entitlementType',
  'isPerEpisode', 'minAge', 'proRateByPartTime', 'resetMonth',
]

function renderSection(onRefresh = vi.fn()) {
  return render(
    <EntitlementSection
      configId="cfg-1"
      entitlements={[careDayRow]}
      readOnly={false}
      onRefresh={onRefresh}
    />,
  )
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('EntitlementSection — the read side', () => {
  it('the spec Entitlement row REQUIRES fullDayOnly (the S118 read-side additive gain)', () => {
    expectTypeOf<Entitlement['fullDayOnly']>().toEqualTypeOf<boolean>()
  })

  it('renders the row via the runtime-guarded label helpers (open-string wire type)', () => {
    captureCalls()
    renderSection()
    expect(screen.getByText('Omsorgsdag')).toBeInTheDocument()
    expect(screen.getByText('Straks')).toBeInTheDocument()
  })
})

describe('EntitlementSection — wire pins for the three mutations', () => {
  it('create → POST /api/agreement-configs/cfg-1/entitlements, UNCONDITIONED, and the W2 pin: NO fullDayOnly / effectiveFrom in the body', async () => {
    const calls = captureCalls(() => ({ body: careDayRow }))
    renderSection()

    fireEvent.click(screen.getByRole('button', { name: 'Tilfoej berettigelse' }))
    await screen.findByText('Tilfoej berettigelse', { selector: 'h2' })
    fireEvent.click(screen.getByRole('button', { name: 'Opret' }))

    await waitFor(() => expect(calls.length).toBe(1))
    const post = calls[0]
    expect(post.url).toBe('/api/agreement-configs/cfg-1/entitlements')
    expect(post.method).toBe('POST')
    expect(post.headers['If-Match']).toBeUndefined()
    expect(post.headers['If-None-Match']).toBeUndefined()
    expect(Object.keys(post.body as Record<string, unknown>).sort()).toEqual(EXPECTED_BODY_KEYS)
    expect(post.body).not.toHaveProperty('fullDayOnly')
    expect(post.body).not.toHaveProperty('effectiveFrom')
  })

  it('update (the SANCTIONED DEFERRED legacy PUT) → If-Match "<version>" kept; bytes pinned: NO fullDayOnly (the W2 422 dead-end) and NO effectiveFrom (the binder 400 dead-end)', async () => {
    const calls = captureCalls(() => ({ body: careDayRow }))
    renderSection()

    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    await screen.findByText('Rediger berettigelse')
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))

    await waitFor(() => expect(calls.length).toBe(1))
    const put = calls[0]
    expect(put.url).toBe('/api/agreement-configs/cfg-1/entitlements/e-1')
    expect(put.method).toBe('PUT')
    expect(put.headers['If-Match']).toBe('"4"')
    expect(Object.keys(put.body as Record<string, unknown>).sort()).toEqual(EXPECTED_BODY_KEYS)
    expect(put.body).not.toHaveProperty('fullDayOnly')
    expect(put.body).not.toHaveProperty('effectiveFrom')
  })

  it('delete → the TYPED DELETE with If-Match, NO body, 204', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const calls = captureCalls(() => ({ status: 204 }))
    const onRefresh = vi.fn()
    renderSection(onRefresh)

    fireEvent.click(screen.getByRole('button', { name: 'Slet' }))

    await waitFor(() => expect(calls.length).toBe(1))
    const del = calls[0]
    expect(del.url).toBe('/api/agreement-configs/cfg-1/entitlements/e-1')
    expect(del.method).toBe('DELETE')
    expect(del.headers['If-Match']).toBe('"4"')
    expect(del.body).toBeUndefined()
    await waitFor(() => expect(onRefresh).toHaveBeenCalled())
    confirmSpy.mockRestore()
  })
})
