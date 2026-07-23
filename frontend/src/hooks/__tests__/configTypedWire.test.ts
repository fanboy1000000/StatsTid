// S118 / TASK-11801 — WIRE-level pins for the Pass-5 bucket-A typed switch
// (PAT-012). These exercise the REAL typed apiClient / apiFetchWithEtag path
// (stubbed fetch, no hook mock), pinning the byte-equivalence the switch
// promised per the S118 If-Match demand map:
//
//  • every If-Match mutation sends EXACTLY the precondition header it sent
//    before (`If-Match: "<version>"` composed via lib/etag.ts);
//  • CREATES are UNCONDITIONED — no If-Match and no If-None-Match anywhere in
//    this bucket;
//  • URLs (interpolation + query building) are byte-identical to the legacy
//    string-built forms;
//  • request bodies are byte-identical — including the wage-type-mapping
//    update PUT, whose omission of `effectiveFrom` was the S118 NAMED
//    DEFERRED DEFECT (a live binder-400 dead-end) and is, post-S121 ruling
//    #1, a DELIBERATE server-default omission (`effectiveFrom` is optional on
//    the wire; the server stamps today). The call GRADUATED to the typed form
//    in S121 with the SAME bytes — the 6-key pin REMAINS;
//  • the ADDITIVE-surfacing pin: the 5 compliance fields
//    (maxDailyHours / minimumRestHours / restPeriodDerogationAllowed /
//    weeklyMaxHoursReferencePeriod / voluntaryUnsocialHoursAllowed) the old
//    hand-written interface OMITTED, plus the embedded entitlements'
//    `fullDayOnly`, flow through the parsed spec types.
import { describe, it, expect, expectTypeOf, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import {
  useAgreementConfigs,
  useAgreementConfig,
  useAgreementConfigActions,
  type AgreementConfig,
  type AgreementConfigWithEntitlements,
} from '../useAgreementConfigs'
import { usePositionOverrides } from '../usePositionOverrides'
import { useWageTypeMappings } from '../useWageTypeMappings'
import { useEntitlementConfigList, useEntitlementConfigActions } from '../useEntitlementConfig'

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

/** Capture every fetch call's (url, method, parsed body, headers); respond
    per (url, method) with { status, body, etag }. */
function captureCalls(
  respond: (url: string, method: string) => { status?: number; body?: unknown; etag?: string } = () => ({}),
) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      headers: toHeaderRecord(init?.headers),
    })
    const { status = 200, body = [], etag } = respond(url, init?.method ?? 'GET')
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

// ── spec-true fixtures ───────────────────────────────────────────────────────

const agreementRow: AgreementConfig = {
  configId: 'cfg-1',
  agreementCode: 'AC',
  okVersion: 'OK24',
  status: 'DRAFT',
  version: 3,
  weeklyNormHours: 37,
  normPeriodWeeks: 1,
  normModel: 'WEEKLY_HOURS',
  annualNormHours: 1924,
  maxFlexBalance: 150,
  flexCarryoverMax: 37,
  hasOvertime: false,
  hasMerarbejde: false,
  overtimeThreshold50: 0,
  overtimeThreshold100: 0,
  eveningSupplementEnabled: false,
  nightSupplementEnabled: false,
  weekendSupplementEnabled: false,
  holidaySupplementEnabled: false,
  eveningStart: 17,
  eveningEnd: 23,
  nightStart: 23,
  nightEnd: 6,
  eveningRate: 0,
  nightRate: 0,
  weekendSaturdayRate: 0,
  weekendSundayRate: 0,
  holidayRate: 0,
  onCallDutyEnabled: false,
  onCallDutyRate: 0,
  callInWorkEnabled: false,
  callInMinimumHours: 3,
  callInRate: 1,
  travelTimeEnabled: false,
  workingTravelRate: 1,
  nonWorkingTravelRate: 0.5,
  // The 5 additively-surfaced compliance fields (the old interface's omission).
  maxDailyHours: 13,
  minimumRestHours: 11,
  restPeriodDerogationAllowed: false,
  weeklyMaxHoursReferencePeriod: 4,
  voluntaryUnsocialHoursAllowed: false,
  createdBy: 'admin',
  createdAt: '2026-04-01T00:00:00Z',
  updatedAt: '2026-04-01T00:00:00Z',
  publishedAt: null,
  archivedAt: null,
  clonedFromId: null,
  description: null,
}

const agreementWithEntitlements: AgreementConfigWithEntitlements = {
  ...agreementRow,
  entitlements: [
    {
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
    },
  ],
  entitlementsReadOnly: false,
}

// ── the additive-surfacing pins (type-level) ─────────────────────────────────

describe('the lie-audit additive pins — the spec types surface what the deleted interfaces omitted', () => {
  it('the 5 compliance fields are REQUIRED members of the spec AgreementConfig', () => {
    expectTypeOf<AgreementConfig['maxDailyHours']>().toEqualTypeOf<number>()
    expectTypeOf<AgreementConfig['minimumRestHours']>().toEqualTypeOf<number>()
    expectTypeOf<AgreementConfig['restPeriodDerogationAllowed']>().toEqualTypeOf<boolean>()
    expectTypeOf<AgreementConfig['weeklyMaxHoursReferencePeriod']>().toEqualTypeOf<number>()
    expectTypeOf<AgreementConfig['voluntaryUnsocialHoursAllowed']>().toEqualTypeOf<boolean>()
  })

  it('the embedded entitlement rows carry the S73 fullDayOnly flag (read-side, required)', () => {
    expectTypeOf<
      AgreementConfigWithEntitlements['entitlements'][number]['fullDayOnly']
    >().toEqualTypeOf<boolean>()
  })
})

// ── useAgreementConfigs / useAgreementConfig (the reads) ─────────────────────

describe('useAgreementConfigs — typed reads hit the exact legacy URLs', () => {
  it('list → GET /api/agreement-configs (no filter)', async () => {
    const calls = captureCalls()
    const { result } = renderHook(() => useAgreementConfigs())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/agreement-configs')
    expect(calls[0].method).toBe('GET')
  })

  it('list → GET /api/agreement-configs?status=ACTIVE (filtered)', async () => {
    const calls = captureCalls()
    const { result } = renderHook(() => useAgreementConfigs('ACTIVE'))
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/agreement-configs?status=ACTIVE')
  })

  it('by-id → GET /api/agreement-configs/{configId}; the 5 compliance fields + fullDayOnly flow through at runtime', async () => {
    const calls = captureCalls(() => ({ body: agreementWithEntitlements, etag: '"3"' }))
    const { result } = renderHook(() => useAgreementConfig('cfg-1'))
    await waitFor(() => expect(result.current.config).not.toBeNull())
    expect(calls[0].url).toBe('/api/agreement-configs/cfg-1')
    expect(calls[0].method).toBe('GET')
    expect(result.current.config?.maxDailyHours).toBe(13)
    expect(result.current.config?.minimumRestHours).toBe(11)
    expect(result.current.config?.weeklyMaxHoursReferencePeriod).toBe(4)
    expect(result.current.config?.entitlements[0].fullDayOnly).toBe(true)
    expect(result.current.config?.etag).toBe('"3"')
  })
})

// ── useAgreementConfigActions (the 5 mutations) ──────────────────────────────

describe('useAgreementConfigActions — precondition fidelity per the demand map', () => {
  const respond = () => ({ body: agreementRow, etag: '"4"' })

  it('createConfig → POST /api/agreement-configs, UNCONDITIONED, body verbatim', async () => {
    const calls = captureCalls(respond)
    const { result } = renderHook(() => useAgreementConfigActions())
    await act(async () => {
      await result.current.createConfig({ ...agreementRow })
    })
    expect(calls[0].url).toBe('/api/agreement-configs')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
    expect(calls[0].body).toEqual({ ...agreementRow })
  })

  it('updateConfig → PUT /api/agreement-configs/{configId} with If-Match exactly as before', async () => {
    const calls = captureCalls(respond)
    const { result } = renderHook(() => useAgreementConfigActions())
    await act(async () => {
      await result.current.updateConfig('cfg-1', '"3"', { ...agreementRow })
    })
    expect(calls[0].url).toBe('/api/agreement-configs/cfg-1')
    expect(calls[0].method).toBe('PUT')
    expect(calls[0].headers['If-Match']).toBe('"3"')
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
  })

  it('cloneConfig → POST /api/agreement-configs/{configId}/clone with the exact legacy query, NO body, UNCONDITIONED', async () => {
    const calls = captureCalls(respond)
    const { result } = renderHook(() => useAgreementConfigActions())
    await act(async () => {
      await result.current.cloneConfig('cfg-1', 'AC', 'OK26')
    })
    expect(calls[0].url).toBe('/api/agreement-configs/cfg-1/clone?agreementCode=AC&okVersion=OK26')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].body).toBeUndefined()
    expect(calls[0].headers['If-Match']).toBeUndefined()
    expect(calls[0].headers['If-None-Match']).toBeUndefined()
  })

  it('cloneConfig with no overrides → no query string at all (legacy byte-equivalence)', async () => {
    const calls = captureCalls(respond)
    const { result } = renderHook(() => useAgreementConfigActions())
    await act(async () => {
      await result.current.cloneConfig('cfg-1')
    })
    expect(calls[0].url).toBe('/api/agreement-configs/cfg-1/clone')
  })

  it('publishConfig → POST …/publish with If-Match, NO body; the spec envelope flows through', async () => {
    const calls = captureCalls(() => ({
      body: { configId: 'cfg-1', status: 'ACTIVE', archivedConfigId: null, publishedAt: '2026-07-20T00:00:00Z' },
      etag: '"4"',
    }))
    const { result } = renderHook(() => useAgreementConfigActions())
    let envelope: Awaited<ReturnType<typeof result.current.publishConfig>> | null = null
    await act(async () => {
      envelope = await result.current.publishConfig('cfg-1', '"3"')
    })
    expect(calls[0].url).toBe('/api/agreement-configs/cfg-1/publish')
    expect(calls[0].method).toBe('POST')
    expect(calls[0].headers['If-Match']).toBe('"3"')
    expect(calls[0].body).toBeUndefined()
    expect(envelope!.status).toBe('ACTIVE')
    expect(envelope!.version).toBe(4)
  })

  it('archiveConfig → POST …/archive with If-Match, NO body', async () => {
    const calls = captureCalls(() => ({
      body: { configId: 'cfg-1', status: 'ARCHIVED', archivedAt: '2026-07-20T00:00:00Z' },
      etag: '"4"',
    }))
    const { result } = renderHook(() => useAgreementConfigActions())
    await act(async () => {
      await result.current.archiveConfig('cfg-1', '"3"')
    })
    expect(calls[0].url).toBe('/api/agreement-configs/cfg-1/archive')
    expect(calls[0].headers['If-Match']).toBe('"3"')
    expect(calls[0].body).toBeUndefined()
  })
})

// ── usePositionOverrides ─────────────────────────────────────────────────────

describe('usePositionOverrides — precondition fidelity per the demand map', () => {
  const overrideRow = {
    overrideId: 'ov-1',
    agreementCode: 'AC',
    okVersion: 'OK24',
    positionCode: 'RESEARCHER',
    status: 'ACTIVE',
    version: 2,
    maxFlexBalance: null,
    flexCarryoverMax: null,
    normPeriodWeeks: 4,
    weeklyNormHours: null,
    createdBy: 'admin',
    createdAt: '2026-04-01T00:00:00Z',
    updatedAt: '2026-04-01T00:00:00Z',
    description: null,
  }

  function overrideCalls() {
    return captureCalls((url, method) => {
      if (method === 'GET') return { body: [overrideRow] }
      if (url.endsWith('/deactivate')) return { body: { overrideId: 'ov-1', status: 'INACTIVE', deactivated: true }, etag: '"3"' }
      if (url.endsWith('/activate')) return { body: { overrideId: 'ov-1', status: 'ACTIVE', activated: true }, etag: '"3"' }
      return { body: overrideRow, etag: '"3"' }
    })
  }

  async function mount() {
    const rendered = renderHook(() => usePositionOverrides())
    await waitFor(() => expect(rendered.result.current.loading).toBe(false))
    return rendered
  }

  it('list → GET /api/admin/position-overrides', async () => {
    const calls = overrideCalls()
    await mount()
    expect(calls[0].url).toBe('/api/admin/position-overrides')
    expect(calls[0].method).toBe('GET')
  })

  it('create → POST /api/admin/position-overrides, UNCONDITIONED, body verbatim', async () => {
    const calls = overrideCalls()
    const { result } = await mount()
    const body = {
      agreementCode: 'AC',
      okVersion: 'OK24',
      positionCode: 'RESEARCHER',
      maxFlexBalance: null,
      flexCarryoverMax: null,
      normPeriodWeeks: 4,
      weeklyNormHours: null,
      description: null,
    }
    await act(async () => { await result.current.create(body) })
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.url).toBe('/api/admin/position-overrides')
    expect(post.headers['If-Match']).toBeUndefined()
    expect(post.headers['If-None-Match']).toBeUndefined()
    expect(post.body).toEqual(body)
  })

  it('update → PUT /api/admin/position-overrides/{overrideId} with If-Match exactly as before', async () => {
    const calls = overrideCalls()
    const { result } = await mount()
    await act(async () => {
      await result.current.update('ov-1', '"2"', {
        agreementCode: 'AC',
        okVersion: 'OK24',
        positionCode: 'RESEARCHER',
        maxFlexBalance: 100,
        flexCarryoverMax: null,
        normPeriodWeeks: 4,
        weeklyNormHours: null,
        description: null,
      })
    })
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.url).toBe('/api/admin/position-overrides/ov-1')
    expect(put.headers['If-Match']).toBe('"2"')
  })

  it('deactivate / activate → POST with If-Match, NO body; the spec envelopes flow through', async () => {
    const calls = overrideCalls()
    const { result } = await mount()
    await act(async () => {
      const deactivated = await result.current.deactivate('ov-1', '"2"')
      expect(deactivated.deactivated).toBe(true)
      const activated = await result.current.activate('ov-1', '"3"')
      expect(activated.activated).toBe(true)
    })
    const posts = calls.filter((c) => c.method === 'POST')
    expect(posts[0].url).toBe('/api/admin/position-overrides/ov-1/deactivate')
    expect(posts[0].headers['If-Match']).toBe('"2"')
    expect(posts[0].body).toBeUndefined()
    expect(posts[1].url).toBe('/api/admin/position-overrides/ov-1/activate')
    expect(posts[1].headers['If-Match']).toBe('"3"')
    expect(posts[1].body).toBeUndefined()
  })
})

// ── useWageTypeMappings ──────────────────────────────────────────────────────

describe('useWageTypeMappings — precondition fidelity + the S121 deliberate-omission pin', () => {
  const mappingRow = {
    timeType: 'NORMAL_WORK',
    wageType: 'SLS_0100',
    okVersion: 'OK24',
    agreementCode: 'AC',
    position: '',
    description: null,
    version: 1,
  }

  function mappingCalls() {
    return captureCalls((_url, method) => {
      if (method === 'GET') return { body: [mappingRow] }
      if (method === 'DELETE') return { status: 204 }
      return { body: mappingRow, etag: '"2"' }
    })
  }

  async function mount() {
    const rendered = renderHook(() => useWageTypeMappings())
    await waitFor(() => expect(rendered.result.current.loading).toBe(false))
    return rendered
  }

  it('list → GET /api/admin/wage-type-mappings', async () => {
    const calls = mappingCalls()
    await mount()
    expect(calls[0].url).toBe('/api/admin/wage-type-mappings')
  })

  it('create → POST, UNCONDITIONED, body verbatim', async () => {
    const calls = mappingCalls()
    const { result } = await mount()
    const body = {
      timeType: 'NORMAL_WORK',
      wageType: 'SLS_0100',
      okVersion: 'OK24',
      agreementCode: 'AC',
      position: '',
      description: null,
    }
    await act(async () => { await result.current.create(body) })
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.url).toBe('/api/admin/wage-type-mappings')
    expect(post.headers['If-Match']).toBeUndefined()
    expect(post.headers['If-None-Match']).toBeUndefined()
    expect(post.body).toEqual(body)
  })

  it('updateMapping (GRADUATED to the typed PUT in S121) → If-Match kept; the bytes pinned: effectiveFrom is ABSENT (ruling #1 — a DELIBERATE server-default omission, no longer a dead-end)', async () => {
    const calls = mappingCalls()
    const { result } = await mount()
    const body = {
      timeType: 'NORMAL_WORK',
      wageType: 'SLS_0200',
      okVersion: 'OK24',
      agreementCode: 'AC',
      position: '',
      description: null,
    }
    await act(async () => { await result.current.updateMapping('"1"', body) })
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.url).toBe('/api/admin/wage-type-mappings')
    expect(put.headers['If-Match']).toBe('"1"')
    // S121 REMAIN pin (rationale rewritten): the payload is BYTE-IDENTICAL
    // across the S121 graduation — exactly these 6 keys, NO effectiveFrom.
    // The omission is no longer the S118 400 dead-end but ruling #1's
    // DELIBERATE server-default: `effectiveFrom` is optional on the wire and
    // the server stamps today.
    expect(Object.keys(put.body as Record<string, unknown>).sort()).toEqual([
      'agreementCode', 'description', 'okVersion', 'position', 'timeType', 'wageType',
    ])
    expect(put.body).not.toHaveProperty('effectiveFrom')
  })

  it('deleteMapping → DELETE with the exact legacy query string + If-Match, NO body, 204', async () => {
    const calls = mappingCalls()
    const { result } = await mount()
    await act(async () => {
      await result.current.deleteMapping('NORMAL_WORK', 'OK24', 'AC', '', '"1"')
    })
    const del = calls.find((c) => c.method === 'DELETE')!
    expect(del.url).toBe(
      '/api/admin/wage-type-mappings?timeType=NORMAL_WORK&okVersion=OK24&agreementCode=AC&position=',
    )
    expect(del.headers['If-Match']).toBe('"1"')
    expect(del.body).toBeUndefined()
  })
})

// ── useEntitlementConfig (admin family) ──────────────────────────────────────

describe('useEntitlementConfig — precondition fidelity per the demand map', () => {
  const configRow = {
    configId: 'ec-1',
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

  function configCalls() {
    return captureCalls((_url, method) => {
      if (method === 'GET') return { body: [configRow] }
      if (method === 'DELETE') return { status: 204 }
      return { body: configRow, etag: '"5"' }
    })
  }

  it('list → GET /api/admin/entitlement-configs', async () => {
    const calls = configCalls()
    const { result } = renderHook(() => useEntitlementConfigList())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/admin/entitlement-configs')
  })

  it('createConfig → POST, UNCONDITIONED; updateConfig → PUT with If-Match; deleteConfig → DELETE with If-Match, no body', async () => {
    const calls = configCalls()
    const { result } = renderHook(() => useEntitlementConfigActions())
    const createBody = {
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
    }
    await act(async () => {
      await result.current.createConfig(createBody)
      await result.current.updateConfig('ec-1', '"4"', createBody)
      await result.current.deleteConfig('ec-1', '"5"')
    })
    const [post, put, del] = calls
    expect(post.url).toBe('/api/admin/entitlement-configs')
    expect(post.method).toBe('POST')
    expect(post.headers['If-Match']).toBeUndefined()
    expect(post.headers['If-None-Match']).toBeUndefined()
    expect(post.body).toEqual(createBody)
    expect(put.url).toBe('/api/admin/entitlement-configs/ec-1')
    expect(put.method).toBe('PUT')
    expect(put.headers['If-Match']).toBe('"4"')
    // S121 UPDATE pin (the S118 presence pin inverted by ruling #1): the
    // primary editor's PUT now OMITS `effectiveFrom` — the server defaults it
    // to today — while `fullDayOnly` (binder-REQUIRED post-ruling #3) still
    // travels round-trip, so a regression toward the child-PUT defect class
    // is still caught.
    expect(put.body).toEqual(createBody)
    expect(put.body).toHaveProperty('fullDayOnly', true)
    expect(put.body).not.toHaveProperty('effectiveFrom')
    expect(del.url).toBe('/api/admin/entitlement-configs/ec-1')
    expect(del.method).toBe('DELETE')
    expect(del.headers['If-Match']).toBe('"5"')
    expect(del.body).toBeUndefined()
  })
})
