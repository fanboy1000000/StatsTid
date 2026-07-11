// S112 / TASK-11202 — typed structured overloads for the body verbs
// (`apiClient.post`/`put`/`delete`) and `apiFetchWithEtag` (PAT-012).
//
// The derivation logic was authored when the committed spec declared ZERO typed
// PUT/DELETE operations; since the S112 retrofit (TASK-11203 regen) the typed
// unions are non-empty (see the phase-pin test, which pins the current truth).
// The logic itself stays proven against a SYNTHETIC `paths`-shaped fixture (mirroring the
// openapi-typescript generated structure) with 200-, 201- and 204-response
// operations plus a multi-verb path. The fixture callables below mirror the
// EXACT overload shapes in `../api` (same exported generic types), so a
// regression in the derivation reds this file under `tsc --noEmit` (both
// `expectTypeOf` mismatches and unused `@ts-expect-error` directives).
//
// The runtime half pins the structured-vs-legacy call discrimination: every
// existing untyped call shape (raw JSON body, RequestInit with pre-stringified
// body, `{ method }`-only init) must keep byte-identical wire behavior.
import { expectTypeOf } from 'vitest'
import { apiClient, apiFetchWithEtag } from '../api'
import type {
  ApiResult,
  ApiResponseWithEtag,
  HasRequiredKey,
  SuccessDataOf,
  TypedPathIn,
  OperationIn,
  RequestBodyOf,
  StructuredOptionsForOp,
  EtagPathIn,
  EtagMethodsIn,
  EtagOptionsIn,
  EtagDataIn,
} from '../api'
import type { paths, components } from '../api-types'

// ─── shared mock harness (same pattern as api.test.ts) ──────────────────────

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach(k => delete mockStorage[k])
})

// ─── the synthetic compile-time fixture spec ─────────────────────────────────

interface Widget {
  id: string
  name: string
  version: number
}
interface WidgetCreateBody {
  name: string
  parentId?: string | null
}
interface WidgetUpdateBody {
  name: string
}

/** A test-local `paths`-shaped map, structurally faithful to the
    openapi-typescript output (`?: never` for absent verbs/params, `content?:
    never` for undeclared response bodies). */
interface FixturePaths {
  '/fixture/widgets': {
    parameters: { query?: never; header?: never; path?: never; cookie?: never }
    get: {
      parameters: {
        query?: { includeArchived?: boolean }
        header?: never
        path?: never
        cookie?: never
      }
      requestBody?: never
      responses: {
        200: { headers: { [name: string]: unknown }; content: { 'application/json': Widget[] } }
      }
    }
    put?: never
    post: {
      parameters: { query?: never; header?: never; path?: never; cookie?: never }
      requestBody: { content: { 'application/json': WidgetCreateBody } }
      responses: {
        201: { headers: { [name: string]: unknown }; content: { 'application/json': Widget } }
      }
    }
    delete?: never
  }
  '/fixture/widgets/{id}': {
    parameters: { query?: never; header?: never; path?: never; cookie?: never }
    get?: never
    put: {
      parameters: { query?: never; header?: never; path: { id: string }; cookie?: never }
      requestBody: { content: { 'application/json': WidgetUpdateBody } }
      responses: {
        200: { headers: { [name: string]: unknown }; content: { 'application/json': Widget } }
      }
    }
    post?: never
    delete: {
      parameters: { query?: never; header?: never; path: { id: string }; cookie?: never }
      requestBody?: never
      responses: { 204: { headers: { [name: string]: unknown }; content?: never } }
    }
  }
  // A grandfathered op: 200 with NO declared content — must be EXCLUDED from
  // every typed union (its wire shape is unknown; typing it would lie).
  '/fixture/widgets/legacy': {
    parameters: { query?: never; header?: never; path?: never; cookie?: never }
    get?: never
    put?: never
    post: {
      parameters: { query?: never; header?: never; path?: never; cookie?: never }
      requestBody?: never
      responses: { 200: { headers: { [name: string]: unknown }; content?: never } }
    }
    delete?: never
  }
}

type WidgetsGetOp = OperationIn<FixturePaths, 'get', '/fixture/widgets'>
type WidgetsPostOp = OperationIn<FixturePaths, 'post', '/fixture/widgets'>
type WidgetPutOp = OperationIn<FixturePaths, 'put', '/fixture/widgets/{id}'>
type WidgetDeleteOp = OperationIn<FixturePaths, 'delete', '/fixture/widgets/{id}'>

// ─── fixture-bound callables — EXACT mirrors of the `../api` overload shapes,
//     instantiated with FixturePaths instead of the real `paths`. `declare`d
//     only (no runtime value): they exist purely for the compiler, so they are
//     referenced exclusively inside never-invoked thunks below. ───────────────

declare function fixturePost<P extends TypedPathIn<FixturePaths, 'post'>>(
  pathKey: P,
  ...args: HasRequiredKey<StructuredOptionsForOp<OperationIn<FixturePaths, 'post', P>>> extends true
    ? [options: StructuredOptionsForOp<OperationIn<FixturePaths, 'post', P>>]
    : [options?: StructuredOptionsForOp<OperationIn<FixturePaths, 'post', P>>]
): Promise<ApiResult<SuccessDataOf<OperationIn<FixturePaths, 'post', P>>>>

declare function fixturePut<P extends TypedPathIn<FixturePaths, 'put'>>(
  pathKey: P,
  ...args: HasRequiredKey<StructuredOptionsForOp<OperationIn<FixturePaths, 'put', P>>> extends true
    ? [options: StructuredOptionsForOp<OperationIn<FixturePaths, 'put', P>>]
    : [options?: StructuredOptionsForOp<OperationIn<FixturePaths, 'put', P>>]
): Promise<ApiResult<SuccessDataOf<OperationIn<FixturePaths, 'put', P>>>>

declare function fixtureDelete<P extends TypedPathIn<FixturePaths, 'delete'>>(
  pathKey: P,
  ...args: HasRequiredKey<StructuredOptionsForOp<OperationIn<FixturePaths, 'delete', P>>> extends true
    ? [options: StructuredOptionsForOp<OperationIn<FixturePaths, 'delete', P>>]
    : [options?: StructuredOptionsForOp<OperationIn<FixturePaths, 'delete', P>>]
): Promise<ApiResult<SuccessDataOf<OperationIn<FixturePaths, 'delete', P>>>>

declare function fixtureEtag<
  P extends EtagPathIn<FixturePaths>,
  M extends EtagMethodsIn<FixturePaths, P>,
>(
  pathKey: P,
  options: EtagOptionsIn<FixturePaths, P, M>,
): Promise<ApiResult<ApiResponseWithEtag<EtagDataIn<FixturePaths, P, M>>>>

// Positive probes: never invoked (the fixture callables have no runtime
// implementation) — their inferred return types carry the assertions.
const probes = {
  createWidget: () => fixturePost('/fixture/widgets', { body: { name: 'w' } }),
  renameWidget: () =>
    fixturePut('/fixture/widgets/{id}', { params: { path: { id: 'w1' } }, body: { name: 'n' } }),
  removeWidget: () => fixtureDelete('/fixture/widgets/{id}', { params: { path: { id: 'w1' } } }),
  etagPut: () =>
    fixtureEtag('/fixture/widgets/{id}', {
      method: 'PUT',
      params: { path: { id: 'w1' } },
      ifMatch: '"3"',
      body: { name: 'n' },
    }),
  etagDelete: () =>
    fixtureEtag('/fixture/widgets/{id}', {
      method: 'DELETE',
      params: { path: { id: 'w1' } },
      ifMatch: '"3"',
    }),
  etagGet: () => fixtureEtag('/fixture/widgets', { method: 'GET' }),
  // S115 / TASK-11502 — the ADDITIVE create-only precondition option: the typed
  // options accept `ifNoneMatch: '*'` (and ONLY the literal '*').
  etagIfNoneMatch: () =>
    fixtureEtag('/fixture/widgets', { method: 'POST', ifNoneMatch: '*', body: { name: 'w' } }),
}

// A valid structured-options VALUE binds against the derived type (runtime-safe).
const putOptionsOk: StructuredOptionsForOp<WidgetPutOp> = {
  params: { path: { id: 'w1' } },
  body: { name: 'n' },
}

// Negative probes — each SINGLE-LINE call MUST be a compile error; if the
// derivation ever loosens, the then-unused @ts-expect-error reds `tsc`.
// prettier-ignore
// @ts-expect-error — a wrong-shape request body is a compile error against the fixture spec
const badBody = () => fixturePut('/fixture/widgets/{id}', { params: { path: { id: 'w1' } }, body: { nam: 'x' } })
// prettier-ignore
// @ts-expect-error — params.path is REQUIRED for a templated route
const missingParams = () => fixturePut('/fixture/widgets/{id}', { body: { name: 'n' } })
// prettier-ignore
// @ts-expect-error — the grandfathered no-content 200 op is NOT an admitted typed path
const legacyExcluded = () => fixturePost('/fixture/widgets/legacy', { body: { name: 'n' } })
// prettier-ignore
// @ts-expect-error — POST is not a declared verb on the {id} path (method discriminant)
const wrongMethod = () => fixtureEtag('/fixture/widgets/{id}', { method: 'POST', params: { path: { id: 'w1' } } })
// prettier-ignore
// @ts-expect-error — a body is FORBIDDEN on the 204 DELETE operation
const bodyOnDelete = () => fixtureEtag('/fixture/widgets/{id}', { method: 'DELETE', params: { path: { id: 'w1' } }, body: { name: 'n' } })
// prettier-ignore
// @ts-expect-error — S115: ifNoneMatch admits ONLY the literal '*', never an entity-tag string
const ifNoneMatchEtagValue = () => fixtureEtag('/fixture/widgets', { method: 'POST', ifNoneMatch: '"5"', body: { name: 'w' } })
// prettier-ignore
// @ts-expect-error — S115 Step-7a: ifMatch and ifNoneMatch are mutually exclusive preconditions
const bothPreconditions = () => fixtureEtag('/fixture/widgets', { method: 'POST', ifMatch: '"3"', ifNoneMatch: '*', body: { name: 'w' } })
// prettier-ignore
// @ts-expect-error — body is forbidden at the options-type level when the op declares none
const deleteOptionsBad: StructuredOptionsForOp<WidgetDeleteOp> = { params: { path: { id: 'w1' } }, body: { name: 'n' } }

const negativeProbes = [badBody, missingParams, legacyExcluded, wrongMethod, bodyOnDelete, ifNoneMatchEtagValue, bothPreconditions]

// ─── real-spec probes (fallback preservation, never invoked) ─────────────────

type UnitResponse = components['schemas']['StatsTid.Backend.Api.Contracts.UnitResponse']
type ForestResponse = components['schemas']['StatsTid.Backend.Api.Contracts.ForestResponse']

const realSpecProbes = {
  // The original S111 typed body op (POST /api/admin/units → 201 JSON); since the
  // S112 retrofit it is one of several — the phase-pin test tracks the full unions.
  typedUnitsPost: () =>
    apiClient.post('/api/admin/units', {
      body: { organisationId: 'STY02', parentUnitId: null, type: 'AFDELING', name: 'Ny enhed' },
    }),
  // Explicit-T callers ride the plain-string fallback untouched (the explicit
  // type argument is not assignable to the path-key union).
  explicitT: () => apiClient.post<{ eventId: string }>('/api/time-entries', { hours: 7 }),
  templateLiteral: () => apiClient.put<void>(`/api/overtime/pre-approval/${'x'}/approve`),
  deleteFallback: () => apiClient.delete<void>('/api/reporting-lines/delegate'),
  // KNOWN+ACCEPTED (task charter): a no-explicit-T literal-path RAW-body call
  // on the typed path silently falls through to the untyped fallback (the raw
  // body does not match the structured options shape). Swept by a later task.
  acceptedFallthrough: () =>
    apiClient.post('/api/admin/units', {
      organisationId: 'STY02',
      parentUnitId: null,
      type: 'AFDELING',
      name: 'x',
    }),
  // Explicit-T etag callers supply ONE type argument against the typed
  // overload's TWO type parameters — eliminated outright, legacy signature holds.
  etagExplicitT: () =>
    apiFetchWithEtag<{ version: number }>('/api/agreement-configs', {
      method: 'POST',
      body: JSON.stringify({}),
    }),
  // A literal typed path WITH the structured form binds the spec type.
  etagTypedGet: () => apiFetchWithEtag('/api/admin/units/forest', { method: 'GET' }),
}

// ─── compile-time assertions ─────────────────────────────────────────────────

describe('S112 typed derivation — compile-time fixtures', () => {
  it('admits only ops declaring a JSON 200/201 or a 204 into the typed unions', () => {
    expectTypeOf<TypedPathIn<FixturePaths, 'post'>>().toEqualTypeOf<'/fixture/widgets'>()
    expectTypeOf<TypedPathIn<FixturePaths, 'put'>>().toEqualTypeOf<'/fixture/widgets/{id}'>()
    expectTypeOf<TypedPathIn<FixturePaths, 'delete'>>().toEqualTypeOf<'/fixture/widgets/{id}'>()
    // the grandfathered no-content 200 path appears in NO union
    expectTypeOf<EtagPathIn<FixturePaths>>().toEqualTypeOf<
      '/fixture/widgets' | '/fixture/widgets/{id}'
    >()
  })

  it('resolves the declared success status: 200/201 → JSON type, 204 → undefined', () => {
    expectTypeOf<SuccessDataOf<WidgetsGetOp>>().toEqualTypeOf<Widget[]>() // 200
    expectTypeOf<SuccessDataOf<WidgetsPostOp>>().toEqualTypeOf<Widget>() // 201
    expectTypeOf<SuccessDataOf<WidgetPutOp>>().toEqualTypeOf<Widget>() // 200
    expectTypeOf<SuccessDataOf<WidgetDeleteOp>>().toEqualTypeOf<undefined>() // 204
  })

  it('binds the request-body type from requestBody.content[application/json]', () => {
    expectTypeOf<RequestBodyOf<WidgetsPostOp>>().toEqualTypeOf<WidgetCreateBody>()
    expectTypeOf<RequestBodyOf<WidgetPutOp>>().toEqualTypeOf<WidgetUpdateBody>()
    expectTypeOf<RequestBodyOf<WidgetDeleteOp>>().toEqualTypeOf<undefined>()
    expect(putOptionsOk.params.path.id).toBe('w1')
  })

  it('makes the options argument required exactly when the op has a required key', () => {
    expectTypeOf<HasRequiredKey<StructuredOptionsForOp<WidgetsPostOp>>>().toEqualTypeOf<true>()
    expectTypeOf<HasRequiredKey<StructuredOptionsForOp<WidgetDeleteOp>>>().toEqualTypeOf<true>()
    expectTypeOf<HasRequiredKey<StructuredOptionsForOp<WidgetsGetOp>>>().toEqualTypeOf<false>()
  })

  it('derives the call return types through the mirrored overload shape', () => {
    expectTypeOf<Awaited<ReturnType<typeof probes.createWidget>>>().toEqualTypeOf<
      ApiResult<Widget>
    >()
    expectTypeOf<Awaited<ReturnType<typeof probes.renameWidget>>>().toEqualTypeOf<
      ApiResult<Widget>
    >()
    expectTypeOf<Awaited<ReturnType<typeof probes.removeWidget>>>().toEqualTypeOf<
      ApiResult<undefined>
    >()
  })

  it('the etag method discriminant selects the operation on a multi-verb path', () => {
    expectTypeOf<EtagMethodsIn<FixturePaths, '/fixture/widgets/{id}'>>().toEqualTypeOf<
      'PUT' | 'DELETE'
    >()
    expectTypeOf<EtagMethodsIn<FixturePaths, '/fixture/widgets'>>().toEqualTypeOf<'GET' | 'POST'>()
    expectTypeOf<EtagMethodsIn<FixturePaths, '/fixture/widgets/legacy'>>().toEqualTypeOf<never>()
    expectTypeOf<EtagDataIn<FixturePaths, '/fixture/widgets/{id}', 'PUT'>>().toEqualTypeOf<Widget>()
    expectTypeOf<
      EtagDataIn<FixturePaths, '/fixture/widgets/{id}', 'DELETE'>
    >().toEqualTypeOf<undefined>()
    expectTypeOf<Awaited<ReturnType<typeof probes.etagPut>>>().toEqualTypeOf<
      ApiResult<ApiResponseWithEtag<Widget>>
    >()
    expectTypeOf<Awaited<ReturnType<typeof probes.etagDelete>>>().toEqualTypeOf<
      ApiResult<ApiResponseWithEtag<undefined>>
    >()
    expectTypeOf<Awaited<ReturnType<typeof probes.etagGet>>>().toEqualTypeOf<
      ApiResult<ApiResponseWithEtag<Widget[]>>
    >()
  })

  it('negative probes stay compile errors (see the @ts-expect-error directives)', () => {
    expect(negativeProbes).toHaveLength(7)
    expect(deleteOptionsBad).toBeDefined()
  })

  it('S115: the ifNoneMatch option is accepted type-level and keeps the derived data type', () => {
    expectTypeOf<Awaited<ReturnType<typeof probes.etagIfNoneMatch>>>().toEqualTypeOf<
      ApiResult<ApiResponseWithEtag<Widget>>
    >()
  })
})

describe('S112 typed derivation — real committed spec', () => {
  it('phase pin: after the S117 Pass-4 drain — 22 POSTs, 14 PUTs, 8 DELETEs (retrofit updates this)', () => {
    // S112 / TASK-11203 — the backend typed 20 ops (units / organizations /
    // users / roles / employee-profiles); the put/delete unions became
    // NON-EMPTY and the post union grew from exactly '/api/admin/units'.
    // S115 / TASK-11502 — Pass 2 (TASK-11501) drained the reporting-lines
    // family + the employee field-endpoints: +5 POSTs, +4 PUTs, +3 DELETEs.
    // S116 / TASK-11602 — Pass 3 (TASK-11600/11601) drained the approval
    // bucket + the delegate trio + the overtime pre-approval quartet:
    // +7 POSTs (5 approval + delegate + overtime create), +2 PUTs (overtime
    // approve/reject), +1 DELETE (delegate — genuine 200 {revokedCount}).
    // S117 / TASK-11702 — Pass 4 (TASK-11701) drained the settlement bucket:
    // +1 PUT (transfer-agreement update, JSON 200) + 4 POSTs (transfer-
    // agreement create, JSON 201; reconcile-payout, JSON 200; termination-
    // payout-request, JSON 201; settlement-reversal, JSON 200). The resolve
    // POST is a DECLARED-bodyless 200 (`content?: never`) — the admission
    // rule excludes it, so it stays off the typed unions by design.
    expectTypeOf<TypedPathIn<paths, 'put'>>().toEqualTypeOf<
      | '/api/admin/organizations/{orgId}'
      | '/api/admin/organizations/{orgId}/move'
      | '/api/admin/users/{userId}'
      | '/api/admin/users/{userId}/unit'
      | '/api/admin/units/{id}'
      | '/api/admin/units/{id}/move'
      | '/api/admin/employee-profiles/{employeeId}'
      | '/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}'
      | '/api/admin/employees/{employeeId}/birth-date'
      | '/api/admin/employees/{employeeId}/employment-start-date'
      | '/api/admin/employees/{employeeId}/employment-end-date'
      | '/api/overtime/pre-approval/{id}/approve'
      | '/api/overtime/pre-approval/{id}/reject'
      | '/api/vacation-transfer-agreements/{employeeId}'
    >()
    expectTypeOf<TypedPathIn<paths, 'delete'>>().toEqualTypeOf<
      | '/api/admin/organizations/{orgId}'
      | '/api/admin/units/{id}'
      | '/api/admin/units/{id}/leaders/{userId}'
      | '/api/admin/employee-profiles/{employeeId}'
      | '/api/admin/reporting-lines/{employeeId}'
      | '/api/admin/reporting-lines/{employeeId}/acting'
      | '/api/admin/reporting-lines/{managerId}/vikar'
      | '/api/reporting-lines/delegate'
    >()
    expectTypeOf<TypedPathIn<paths, 'post'>>().toEqualTypeOf<
      | '/api/admin/organizations'
      | '/api/admin/users'
      | '/api/admin/roles/grant'
      | '/api/admin/roles/revoke'
      | '/api/admin/units'
      | '/api/admin/units/{id}/leaders'
      | '/api/admin/reporting-lines'
      | '/api/admin/reporting-lines/{employeeId}/acting'
      | '/api/admin/reporting-lines/import'
      | '/api/admin/reporting-lines/{employeeId}/remove'
      | '/api/admin/reporting-lines/{managerId}/vikar'
      | '/api/approval/submit'
      | '/api/approval/{periodId}/approve'
      | '/api/approval/{periodId}/employee-approve'
      | '/api/approval/{periodId}/reject'
      | '/api/approval/{periodId}/reopen'
      | '/api/overtime/pre-approval'
      | '/api/reporting-lines/delegate'
      | '/api/vacation-transfer-agreements/{employeeId}'
      | '/api/vacation-settlements/{employeeId}/{entitlementType}/{entitlementYear}/reconcile-payout'
      | '/api/admin/employees/{employeeId}/termination-payout-request'
      | '/api/admin/employees/{employeeId}/settlement-reversal'
    >()
  })

  it('S115: SuccessDataOf resolves the homogeneous 201-or-200 conditional POSTs to the ONE shared type', () => {
    // The 2 conditional POSTs (assign line / assign acting) declare BOTH a 201
    // and a 200 with the SAME schema $ref — the derivation picks the 200 branch
    // first, and both branches carry the identical `ReportingLineResponse`, so
    // the same-T union is naturally a single type (the S115 matcher-extension
    // counterpart on the client side).
    type ReportingLineResponse =
      components['schemas']['StatsTid.Backend.Api.Contracts.ReportingLineResponse']
    expectTypeOf<
      SuccessDataOf<OperationIn<paths, 'post', '/api/admin/reporting-lines'>>
    >().toEqualTypeOf<ReportingLineResponse>()
    expectTypeOf<
      SuccessDataOf<OperationIn<paths, 'post', '/api/admin/reporting-lines/{employeeId}/acting'>>
    >().toEqualTypeOf<ReportingLineResponse>()
  })

  it('fallback preservation: existing call shapes keep their exact types', () => {
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.typedUnitsPost>>>().toEqualTypeOf<
      ApiResult<UnitResponse>
    >()
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.explicitT>>>().toEqualTypeOf<
      ApiResult<{ eventId: string }>
    >()
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.templateLiteral>>>().toEqualTypeOf<
      ApiResult<void>
    >()
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.deleteFallback>>>().toEqualTypeOf<
      ApiResult<void>
    >()
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.acceptedFallthrough>>>().toEqualTypeOf<
      ApiResult<unknown>
    >()
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.etagExplicitT>>>().toEqualTypeOf<
      ApiResult<ApiResponseWithEtag<{ version: number }>>
    >()
    expectTypeOf<Awaited<ReturnType<typeof realSpecProbes.etagTypedGet>>>().toEqualTypeOf<
      ApiResult<ApiResponseWithEtag<ForestResponse>>
    >()
  })
})

// ─── runtime behavior ────────────────────────────────────────────────────────

// The runtime is type-erased, so structured calls against SYNTHETIC paths go
// through loose casts — this exercises the shared implementation directly.
type LooseBodyCall = (pathKey: string, options?: unknown) => Promise<ApiResult<unknown>>
const structuredPost = apiClient.post as unknown as LooseBodyCall
const structuredPut = apiClient.put as unknown as LooseBodyCall
const structuredDelete = apiClient.delete as unknown as LooseBodyCall
const looseEtag = apiFetchWithEtag as unknown as (
  url: string,
  init?: unknown,
) => Promise<ApiResult<ApiResponseWithEtag<unknown>>>

describe('S112 apiClient body verbs — structured runtime behavior', () => {
  it('POST: interpolates path params, appends the query, stringifies the body once', async () => {
    mockStorage['statstid_token'] = 'mytoken'
    mockFetch.mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ id: 'w1' }) })
    await structuredPost('/fixture/widgets/{id}/clone', {
      params: { path: { id: 'a b' } },
      query: { dryRun: true, skip: undefined },
      body: { name: 'x' },
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/fixture/widgets/a%20b/clone?dryRun=true',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ name: 'x' }),
        headers: expect.objectContaining({ Authorization: 'Bearer mytoken' }),
      }),
    )
  })

  it('POST /api/admin/units (the real typed op): sends the body, parses the 201 JSON', async () => {
    const created = { unitId: 'u1', organisationId: 'STY02', parentUnitId: null, type: 'AFDELING', name: 'Ny enhed', version: 1 }
    mockFetch.mockResolvedValueOnce({ ok: true, status: 201, json: async () => created })
    const result = await apiClient.post('/api/admin/units', {
      body: { organisationId: 'STY02', parentUnitId: null, type: 'AFDELING', name: 'Ny enhed' },
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/admin/units',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ organisationId: 'STY02', parentUnitId: null, type: 'AFDELING', name: 'Ny enhed' }),
      }),
    )
    expect(result.ok).toBe(true)
    if (result.ok) {
      expectTypeOf(result.data).toEqualTypeOf<UnitResponse>()
      expect(result.data).toEqual(created)
    }
  })

  it('PUT: a 204 response yields ok with undefined data (matches the 204-aware type)', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204 })
    const result = await structuredPut('/fixture/things/{id}', {
      params: { path: { id: 't1' } },
      body: { name: 'n' },
    })
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data).toBeUndefined()
    expect(mockFetch).toHaveBeenCalledWith(
      '/fixture/things/t1',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ name: 'n' }) }),
    )
  })

  it('DELETE: a structured call with no body sends none', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204 })
    await structuredDelete('/fixture/things/{id}', { params: { path: { id: 't1' } } })
    expect(mockFetch).toHaveBeenCalledWith(
      '/fixture/things/t1',
      expect.objectContaining({ method: 'DELETE', body: undefined }),
    )
  })

  it('throws synchronously on a missing path param (pre-request guard)', () => {
    expect(() =>
      structuredPost('/fixture/widgets/{id}/clone', { params: { path: {} }, body: { name: 'x' } }),
    ).toThrow(/missing path param 'id'/)
  })

  it('fallback: a raw JSON body with domain keys passes through byte-identically', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({}) })
    await apiClient.post('/api/test', { name: 'test', organisationId: 'o1' })
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test',
      expect.objectContaining({ body: JSON.stringify({ name: 'test', organisationId: 'o1' }) }),
    )
  })

  it('fallback: an EMPTY raw body {} is NOT misread as structured (employee-approve shape)', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({}) })
    await apiClient.post('/api/test', {})
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test',
      expect.objectContaining({ body: '{}' }),
    )
  })

  it('documented residual: a raw body of ONLY structured keys is read as structured', async () => {
    // No such caller exists (verified S112); this pins the discriminator edge
    // honestly rather than hiding it. `{ body: x }` sends x, not `{ body: x }`.
    mockFetch.mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({}) })
    await structuredPost('/api/test', { body: { a: 1 } })
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test',
      expect.objectContaining({ body: JSON.stringify({ a: 1 }) }),
    )
  })
})

describe('S112 apiFetchWithEtag — structured runtime behavior', () => {
  it('PUT: interpolates params, threads ifMatch as If-Match, stringifies the body once', async () => {
    mockStorage['statstid_token'] = 'mytoken'
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"6"' }),
      json: async () => ({ id: 't1', version: 6 }),
    })
    const result = await looseEtag('/fixture/things/{id}', {
      method: 'PUT',
      params: { path: { id: 'a/b' } },
      ifMatch: '"5"',
      body: { name: 'n' },
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/fixture/things/a%2Fb',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ name: 'n' }),
        headers: expect.objectContaining({
          'If-Match': '"5"',
          Authorization: 'Bearer mytoken',
          'Content-Type': 'application/json',
        }),
      }),
    )
    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.data.etag).toBe('"6"')
      expect(result.data.status).toBe(200)
    }
  })

  it('412: the structured form preserves the parsed stale-body protocol (ADR-019)', async () => {
    const stale = { error: 'Concurrency precondition failed', expectedVersion: 3, actualVersion: 5 }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify(stale),
    })
    const result = await looseEtag('/fixture/things/{id}', {
      method: 'PUT',
      params: { path: { id: 't1' } },
      ifMatch: '"3"',
      body: { name: 'n' },
    })
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(412)
      expect(result.body).toEqual(stale)
    }
  })

  it('DELETE 204: undefined data, null etag, no body sent', async () => {
    mockFetch.mockResolvedValueOnce({ ok: true, status: 204, headers: new Headers() })
    const result = await looseEtag('/fixture/things/{id}', {
      method: 'DELETE',
      params: { path: { id: 't1' } },
      ifMatch: '"2"',
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/fixture/things/t1',
      expect.objectContaining({ method: 'DELETE', body: undefined }),
    )
    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.data.data).toBeUndefined()
      expect(result.data.etag).toBeNull()
      expect(result.data.status).toBe(204)
    }
  })

  it('S115 ifNoneMatch: emits If-None-Match (and no If-Match) on the wire', async () => {
    mockStorage['statstid_token'] = 'mytoken'
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 201,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => ({ id: 'w9', version: 1 }),
    })
    await looseEtag('/fixture/widgets', {
      method: 'POST',
      ifNoneMatch: '*',
      body: { name: 'w' },
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/fixture/widgets',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ name: 'w' }),
        headers: expect.objectContaining({
          'If-None-Match': '*',
          Authorization: 'Bearer mytoken',
        }),
      }),
    )
    const init = mockFetch.mock.calls[0][1] as { headers: Record<string, string> }
    expect(init.headers['If-Match']).toBeUndefined()
  })

  it('S115 Step-7a: supplying BOTH ifMatch and ifNoneMatch throws (mutually exclusive)', async () => {
    // The options type rejects the combination (see the negative probes); this
    // pins the runtime backstop for non-tsc callers. No fetch may be issued.
    await expect(
      looseEtag('/fixture/widgets', {
        method: 'POST',
        ifMatch: '"3"',
        ifNoneMatch: '*',
        body: { name: 'w' },
      }),
    ).rejects.toThrow(/mutually exclusive/)
    expect(mockFetch).not.toHaveBeenCalled()
  })

  it('legacy RequestInit with a pre-stringified body and NO headers is NOT double-stringified', async () => {
    // The EntitlementSection / useAgreementConfigs create shape: { method, body: string }.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({}),
    })
    await apiFetchWithEtag('/api/test-legacy', {
      method: 'POST',
      body: JSON.stringify({ a: 1 }),
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test-legacy',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ a: 1 }) }),
    )
  })

  it('legacy { method } alone routes through the legacy path unchanged (profileApi shape)', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"1"' }),
      json: async () => ({ v: 1 }),
    })
    const result = await apiFetchWithEtag<{ v: number }>('/api/test-legacy', { method: 'GET' })
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test-legacy',
      expect.objectContaining({ method: 'GET' }),
    )
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.etag).toBe('"1"')
  })
})
