// S118 / TASK-11801 — WIRE-level pins for the child-entitlement sub-resource
// switch in EntitlementSection (PAT-012). Component-level (fetch stubbed).
// S121 / TASK-12101 — the S118 NAMED DEFERRED DEFECT is FIXED and the pins
// flipped DELIBERATELY (defer-pin-never-silence):
//
//  • create POST → the TYPED interpolated URL, UNCONDITIONED (no If-Match /
//    If-None-Match); the body now carries the DERIVED `fullDayOnly` (forced
//    true for CARE_DAY/SENIOR_DAY, false otherwise — ruling #2; the S118
//    absence pin FLIPPED to presence);
//  • update PUT (GRADUATED to the typed form in S121) → If-Match `"<version>"`
//    kept; the body now carries the PRESERVED `fullDayOnly` (the edited row's
//    current value, type-forced floor — ruling #2; the S118 absence pin
//    FLIPPED to presence);
//  • `effectiveFrom` stays ABSENT from BOTH bodies — no longer a dead-end but
//    a DELIBERATE server-default omission (ruling #1: the server owns today);
//    these absence pins REMAIN;
//  • delete DELETE → the TYPED form, If-Match kept, NO body, 204;
//  • the read-side pin: the spec `Entitlement` row REQUIRES `fullDayOnly`.
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

// S121 — a NON-forced row (VACATION) whose fullDayOnly is TRUE: legal under
// the DB CHECK (which only FORCES the two care/senior types) and the fixture
// that distinguishes PRESERVE (round-trip the row value) from DERIVE (which
// would send false for a non-forced type).
const vacationFullDayRow: Entitlement = {
  ...careDayRow,
  configId: 'e-2',
  entitlementType: 'VACATION',
  annualQuota: 25,
  fullDayOnly: true,
  version: 7,
}

// S121 — 10 keys: `fullDayOnly` JOINED both bodies (the S118 9-key pin
// FLIPPED); `effectiveFrom` stays deliberately absent (ruling #1).
const EXPECTED_BODY_KEYS = [
  'accrualModel', 'annualQuota', 'carryoverMax', 'description', 'entitlementType',
  'fullDayOnly', 'isPerEpisode', 'minAge', 'proRateByPartTime', 'resetMonth',
]

function renderSection(onRefresh = vi.fn(), rows: Entitlement[] = [careDayRow]) {
  return render(
    <EntitlementSection
      configId="cfg-1"
      entitlements={rows}
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
  it('create → POST /api/agreement-configs/cfg-1/entitlements, UNCONDITIONED; the S121 FLIP: fullDayOnly PRESENT (derived false for the non-forced VACATION); effectiveFrom stays a DELIBERATE server-default omission', async () => {
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
    // S121 FLIP (was `.not.toHaveProperty('fullDayOnly')`): the create body
    // DERIVES the flag — the default VACATION type is non-forced → false.
    expect(post.body).toHaveProperty('fullDayOnly', false)
    // REMAIN (rationale rewritten): the omission is no longer a dead-end but
    // ruling #1's DELIBERATE server-default — the server stamps today.
    expect(post.body).not.toHaveProperty('effectiveFrom')
  })

  it('create with a FORCED type (CARE_DAY) → the derived fullDayOnly is TRUE (ruling #2, the type-forced derivation)', async () => {
    const calls = captureCalls(() => ({ body: careDayRow }))
    renderSection()

    fireEvent.click(screen.getByRole('button', { name: 'Tilfoej berettigelse' }))
    await screen.findByText('Tilfoej berettigelse', { selector: 'h2' })
    fireEvent.change(screen.getByLabelText(/^Type/), { target: { value: 'CARE_DAY' } })
    fireEvent.click(screen.getByRole('button', { name: 'Opret' }))

    await waitFor(() => expect(calls.length).toBe(1))
    const post = calls[0]
    expect(post.method).toBe('POST')
    expect((post.body as Record<string, unknown>).entitlementType).toBe('CARE_DAY')
    expect(post.body).toHaveProperty('fullDayOnly', true)
    expect(post.body).not.toHaveProperty('effectiveFrom')
  })

  it('update (GRADUATED to the typed PUT in S121) → If-Match "<version>" kept; the S121 FLIP: fullDayOnly PRESENT (preserved — true for the forced CARE_DAY row); effectiveFrom stays a DELIBERATE server-default omission', async () => {
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
    // S121 FLIP (was `.not.toHaveProperty('fullDayOnly')`): the update body
    // PRESERVES the row's flag; CARE_DAY is also type-forced → true.
    expect(put.body).toHaveProperty('fullDayOnly', true)
    // REMAIN (rationale rewritten): ruling #1's deliberate server-default —
    // the previously-binder-required member is now optional and NOT sent.
    expect(put.body).not.toHaveProperty('effectiveFrom')
  })

  it('update of a NON-forced row (VACATION, fullDayOnly:true) → the row value round-trips (PRESERVE, not re-derive — ruling #2)', async () => {
    const calls = captureCalls(() => ({ body: vacationFullDayRow }))
    renderSection(vi.fn(), [vacationFullDayRow])

    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    await screen.findByText('Rediger berettigelse')
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))

    await waitFor(() => expect(calls.length).toBe(1))
    const put = calls[0]
    expect(put.url).toBe('/api/agreement-configs/cfg-1/entitlements/e-2')
    expect(put.method).toBe('PUT')
    expect(put.headers['If-Match']).toBe('"7"')
    // Derivation would send FALSE for the non-forced VACATION — the TRUE here
    // proves the edited row's current value is what travels.
    expect((put.body as Record<string, unknown>).entitlementType).toBe('VACATION')
    expect(put.body).toHaveProperty('fullDayOnly', true)
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
