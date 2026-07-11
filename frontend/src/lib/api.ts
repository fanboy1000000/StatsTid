import type {
  SkemaRowPreferenceAbsenceType,
  SkemaRowPreferenceProject,
  SkemaRowPreferences,
} from '../types'
// S111 / TASK-11102 — the generated OpenAPI path map (committed; run `npm run
// gen:api` to regenerate from ../docs/api/openapi.json). This is the single
// source the typed `apiClient.get(pathKey, …)` overload derives response shapes
// from, structurally closing the S97→S99→S100 "fetchEnheder" shape-mismatch class
// for the wired reads.
import type { paths } from './api-types'

const TOKEN_KEY = 'statstid_token'
const USER_KEY = 'statstid_user'

// S25 / TASK-2506 (ADR-019 pending): error variant carries optional `body` so
// callers of `apiFetchWithEtag` can inspect 412 / 400 structured payloads
// without re-consuming the response stream. Existing `apiClient` callers ignore
// the new field — additive only.
export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; error: string; status: number; body?: unknown }

/**
 * Successful header-aware response shape (S25 / TASK-2506).
 *
 * `etag` is the raw `Response.headers.get('ETag')` value — RFC 7232 quoted form
 * (`"<n>"`) or null. Callers feed this through `parseVersionFromETag` /
 * `resolveEtag` from `lib/etag.ts` rather than reaching into the wire string.
 */
export type ApiResponseWithEtag<T> = {
  data: T
  etag: string | null
  status: number
}

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function handle401() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
  window.location.reload()
}

async function request<T>(method: string, path: string, body?: unknown): Promise<ApiResult<T>> {
  const token = getToken()
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  try {
    const res = await fetch(path, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })

    if (res.status === 401) {
      handle401()
      return { ok: false, error: 'Unauthorized', status: 401 }
    }

    if (!res.ok) {
      let errorMessage = `HTTP ${res.status}`
      try {
        const errorBody = await res.text()
        if (errorBody) {
          errorMessage = errorBody
        }
      } catch {
        // Use default error message
      }
      return { ok: false, error: errorMessage, status: res.status }
    }

    // Handle 204 No Content
    if (res.status === 204) {
      return { ok: true, data: undefined as T }
    }

    const data = await res.json() as T
    return { ok: true, data }
  } catch (e) {
    return { ok: false, error: String(e), status: 0 }
  }
}

// ════════════════════════════════════════════════════════════════════════════
// S111 / TASK-11102 — the STRUCTURED typed `get` call shape (the single typed
// client; NO second HTTP client). The response type is DERIVED from the OpenAPI
// path key, so a stale FE field-access on a wired read is a `tsc` error rather
// than a silent prod break. A raw `keyof paths` key cannot type a templated or
// query path (Step-0b BLOCKER), so the call shape is
// `get(pathKey, { params?: { path }, query? })`: `apiClient` interpolates
// `params.path` into the URL and appends `query`, both type-bound to `pathKey`.
//
// OVERLOAD ORDERING (Step-0b CRITICAL): the typed overload is declared FIRST and
// is constrained to `GetPath` (the union of literal GET path keys). The ~130
// existing `get<ExplicitT>(stringPath)` callers supply an explicit type argument
// that is NOT assignable to `GetPath`, so that overload is not a candidate and
// they fall through to the plain-string fallback overload — no mass retrofit.
// ════════════════════════════════════════════════════════════════════════════

/** The union of path keys in the generated spec that expose a GET with a JSON 200. */
type GetPath = {
  [P in keyof paths]: paths[P] extends {
    get: { responses: { 200: { content: { 'application/json': unknown } } } }
  }
    ? P
    : never
}[keyof paths]

/** The JSON 200 body type for a GET path key. */
type GetResponse<P extends GetPath> = paths[P] extends {
  get: { responses: { 200: { content: { 'application/json': infer R } } } }
}
  ? R
  : never

type GetParameters<P extends GetPath> = paths[P] extends { get: { parameters: infer Pa } }
  ? Pa
  : never

/** The `path` params object for a GET key (`undefined` when the route is literal —
    the generated shape is `path?: never`, which normalizes to `undefined`). */
type GetPathParams<P extends GetPath> = GetParameters<P> extends { path?: infer X }
  ? [X] extends [never]
    ? undefined
    : X
  : undefined

/** The `query` params object for a GET key (`undefined` when there is no query). */
type GetQueryParams<P extends GetPath> = GetParameters<P> extends { query?: infer X }
  ? [X] extends [never]
    ? undefined
    : X
  : undefined

/** The structured options for a typed GET: `params.path` is REQUIRED for a
    templated route, FORBIDDEN for a literal one; `query` is optional when present. */
type GetOptions<P extends GetPath> = (GetPathParams<P> extends undefined
  ? { params?: undefined }
  : { params: { path: GetPathParams<P> } }) &
  (GetQueryParams<P> extends undefined ? { query?: undefined } : { query?: GetQueryParams<P> })

/** Whether a type has at least one required (non-`undefined`) property — used to
    make the `options` argument required for templated routes, optional otherwise.
    (Exported S112 so the fixture harness can mirror the overload shape.) */
export type HasRequiredKey<T> = {
  [K in keyof T]-?: undefined extends T[K] ? never : K
}[keyof T] extends never
  ? false
  : true

/** The loose runtime shape the implementation consumes (hidden from callers). */
type GetCallOptions = {
  params?: { path?: Record<string, unknown> }
  query?: Record<string, unknown>
}

/** Build the request URL: interpolate `{token}` path params, then append the
    query. (S112: generalized from the S111 GET-only helper — `caller` labels
    the error message for whichever typed entry point interpolated the URL.) */
function buildUrl(
  caller: string,
  pathKey: string,
  options?: { params?: { path?: Record<string, unknown> }; query?: Record<string, unknown> },
): string {
  let url = pathKey
  const pathParams = options?.params?.path
  if (pathParams) {
    url = url.replace(/\{([^}]+)\}/g, (_match, key: string) => {
      const value = pathParams[key]
      if (value === undefined || value === null) {
        throw new Error(`${caller}: missing path param '${key}' for '${pathKey}'`)
      }
      return encodeURIComponent(String(value))
    })
  }
  const query = options?.query
  if (query) {
    const search = new URLSearchParams()
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null) search.append(key, String(value))
    }
    const qs = search.toString()
    if (qs) url += (url.includes('?') ? '&' : '?') + qs
  }
  return url
}

// Typed overload (FIRST): binds the response type from a spec path key. The
// `options` argument is required only when the route has required path params.
function apiGet<P extends GetPath>(
  pathKey: P,
  ...args: HasRequiredKey<GetOptions<P>> extends true
    ? [options: GetOptions<P>]
    : [options?: GetOptions<P>]
): Promise<ApiResult<GetResponse<P>>>
// Fallback overload (SECOND): the plain string path for non-retrofitted callers,
// who supply an explicit `T` not assignable to `GetPath`.
function apiGet<T = unknown>(path: string): Promise<ApiResult<T>>
function apiGet(pathKey: string, options?: GetCallOptions): Promise<ApiResult<unknown>> {
  return request<unknown>('GET', buildUrl('apiClient.get', pathKey, options))
}

// ════════════════════════════════════════════════════════════════════════════
// S112 / TASK-11202 — typed structured overloads for the BODY verbs
// (`post`/`put`/`delete`) and `apiFetchWithEtag`, extending the S111 typed-`get`
// pattern (PAT-012). The derivation is GENERIC over a `paths`-shaped map so the
// same type logic is provable against synthetic compile-time fixtures (see
// `__tests__/api-typed-overloads.test.ts`). Against the CURRENT committed spec
// the `put`/`delete` unions are `never` (no PUT/DELETE declares a typed success
// yet) — the retrofit phases populate them with NO further client changes.
//
// ADMISSION RULE (success-status-aware): an operation joins a typed union only
// when it declares a JSON 200, a JSON 201, or a 204. The 204 (and only the 204)
// types as `undefined` data — matching `request`'s runtime 204 branch. A
// grandfathered `content?: never` 200 (the ~130 undeclared ops) is EXCLUDED:
// its wire shape is undeclared, so typing it as anything would relocate the
// S97→S100 false-green rather than close it.
// ════════════════════════════════════════════════════════════════════════════

type MethodKey = 'get' | 'post' | 'put' | 'delete'

/** Success-status-aware response data for an operation: JSON 200/201 → that
    JSON type; a declared 204 (no content) → `undefined`; anything else →
    `never` (the op is untyped and stays on the plain-string fallback). */
export type SuccessDataOf<Op> = Op extends {
  responses: { 200: { content: { 'application/json': infer R } } }
}
  ? R
  : Op extends { responses: { 201: { content: { 'application/json': infer R } } } }
    ? R
    : Op extends { responses: { 204: unknown } }
      ? undefined
      : never

/** The union of path keys in `Paths` whose `M` operation passes the admission
    rule above — the body-verb generalization of the S111 `GetPath`. */
export type TypedPathIn<Paths, M extends MethodKey> = {
  [P in keyof Paths]: Paths[P] extends Record<M, infer Op>
    ? [SuccessDataOf<Op>] extends [never]
      ? never
      : P
    : never
}[keyof Paths]

/** The `M` operation object at path `P` (feeds the option/response derivers). */
export type OperationIn<Paths, M extends MethodKey, P extends keyof Paths> = Paths[P] extends Record<
  M,
  infer Op
>
  ? Op
  : never

/** The declared JSON request-body type of an operation; `undefined` when the
    operation takes no body (the generated `requestBody?: never`). */
export type RequestBodyOf<Op> = Op extends {
  requestBody: { content: { 'application/json': infer B } }
}
  ? B
  : undefined

type ParametersOf<Op> = Op extends { parameters: infer Pa } ? Pa : never

/** The `path` params object of an operation (`undefined` when the route is
    literal — the generated `path?: never` normalizes to `undefined`). */
export type PathParamsOf<Op> = ParametersOf<Op> extends { path?: infer X }
  ? [X] extends [never]
    ? undefined
    : X
  : undefined

/** The `query` params object of an operation (`undefined` when absent). */
export type QueryParamsOf<Op> = ParametersOf<Op> extends { query?: infer X }
  ? [X] extends [never]
    ? undefined
    : X
  : undefined

/** The structured options for a typed body-verb call: `params.path` REQUIRED
    for a templated route, FORBIDDEN for a literal one; `query` optional when
    declared; `body` REQUIRED when the operation declares a JSON request body,
    FORBIDDEN otherwise. */
export type StructuredOptionsForOp<Op> = (PathParamsOf<Op> extends undefined
  ? { params?: undefined }
  : { params: { path: PathParamsOf<Op> } }) &
  (QueryParamsOf<Op> extends undefined ? { query?: undefined } : { query?: QueryParamsOf<Op> }) &
  (RequestBodyOf<Op> extends undefined ? { body?: undefined } : { body: RequestBodyOf<Op> })

// The real-spec bindings (fully evaluated literal-key unions).
type PostPath = TypedPathIn<paths, 'post'>
type PutPath = TypedPathIn<paths, 'put'>
type DeletePath = TypedPathIn<paths, 'delete'>
type PostOptions<P extends PostPath> = StructuredOptionsForOp<OperationIn<paths, 'post', P>>
type PostData<P extends PostPath> = SuccessDataOf<OperationIn<paths, 'post', P>>
type PutOptions<P extends PutPath> = StructuredOptionsForOp<OperationIn<paths, 'put', P>>
type PutData<P extends PutPath> = SuccessDataOf<OperationIn<paths, 'put', P>>
type DeleteOptions<P extends DeletePath> = StructuredOptionsForOp<OperationIn<paths, 'delete', P>>
type DeleteData<P extends DeletePath> = SuccessDataOf<OperationIn<paths, 'delete', P>>

/** The loose runtime shape of a structured body-verb call (hidden from callers). */
type BodyCallOptions = {
  params?: { path?: Record<string, unknown> }
  query?: Record<string, unknown>
  body?: unknown
}

// Runtime discrimination between a STRUCTURED options object and a legacy RAW
// JSON body: an object whose own keys form a NON-EMPTY subset of
// {params, query, body} is structured. `{}` stays a raw body (the existing
// `post(url, {})` employee-approve callers), and every raw domain body in the
// codebase carries at least one domain key outside the set.
// DOCUMENTED RESIDUAL: an untyped call whose raw body consists SOLELY of those
// keys (e.g. `post(path, { body: x })`) would be misread as structured — no
// such caller exists today; new callers should use the typed structured form
// (the S111 convention gate forces every NEW endpoint typed anyway).
const STRUCTURED_BODY_KEYS = new Set(['params', 'query', 'body'])

function isStructuredCall(arg: unknown): arg is BodyCallOptions {
  if (arg === null || typeof arg !== 'object' || Array.isArray(arg)) return false
  const keys = Object.keys(arg)
  return keys.length > 0 && keys.every((k) => STRUCTURED_BODY_KEYS.has(k))
}

// OVERLOAD ORDERING (same rationale as `apiGet`, S111): the typed overload is
// declared FIRST and constrained to the literal path-key union. Every existing
// caller either supplies an explicit `T` (not assignable to the key union → the
// typed overload is not a candidate) or a template-literal/computed URL (not in
// the finite union) — both fall through to the plain-string fallback with ZERO
// call-site edits. KNOWN+ACCEPTED: a no-explicit-T LITERAL-path RAW-body call
// on a typed path (`post('/api/admin/units', rawBody)`) also falls through —
// the raw body does not match the structured options shape — and stays silently
// untyped; a later retrofit task sweeps those call sites.
function apiPost<P extends PostPath>(
  pathKey: P,
  ...args: HasRequiredKey<PostOptions<P>> extends true
    ? [options: PostOptions<P>]
    : [options?: PostOptions<P>]
): Promise<ApiResult<PostData<P>>>
function apiPost<T = unknown>(path: string, body?: unknown): Promise<ApiResult<T>>
function apiPost(pathKey: string, arg?: unknown): Promise<ApiResult<unknown>> {
  return isStructuredCall(arg)
    ? request<unknown>('POST', buildUrl('apiClient.post', pathKey, arg), arg.body)
    : request<unknown>('POST', pathKey, arg)
}

function apiPut<P extends PutPath>(
  pathKey: P,
  ...args: HasRequiredKey<PutOptions<P>> extends true
    ? [options: PutOptions<P>]
    : [options?: PutOptions<P>]
): Promise<ApiResult<PutData<P>>>
function apiPut<T = unknown>(path: string, body?: unknown): Promise<ApiResult<T>>
function apiPut(pathKey: string, arg?: unknown): Promise<ApiResult<unknown>> {
  return isStructuredCall(arg)
    ? request<unknown>('PUT', buildUrl('apiClient.put', pathKey, arg), arg.body)
    : request<unknown>('PUT', pathKey, arg)
}

function apiDelete<P extends DeletePath>(
  pathKey: P,
  ...args: HasRequiredKey<DeleteOptions<P>> extends true
    ? [options: DeleteOptions<P>]
    : [options?: DeleteOptions<P>]
): Promise<ApiResult<DeleteData<P>>>
function apiDelete<T = unknown>(path: string): Promise<ApiResult<T>>
function apiDelete(pathKey: string, arg?: unknown): Promise<ApiResult<unknown>> {
  return isStructuredCall(arg)
    ? request<unknown>('DELETE', buildUrl('apiClient.delete', pathKey, arg), arg.body)
    : request<unknown>('DELETE', pathKey)
}

export const apiClient = {
  get: apiGet,
  post: apiPost,
  put: apiPut,
  delete: apiDelete,
}

/**
 * Header-aware fetch variant (S25 / TASK-2506, ADR-019 pending).
 *
 * Identical auth + 401 handling to `apiClient.*` but exposes the raw `ETag`
 * response header alongside the parsed body. Callers compose `If-Match` /
 * `If-None-Match` themselves via `init.headers`. The companion helpers in
 * `lib/etag.ts` (`parseVersionFromETag`, `formatVersionAsIfMatch`,
 * `resolveEtag`) handle wire-format parsing — this module deliberately does
 * NOT pre-parse the version so the caller can choose between header-only
 * and header-with-body-fallback strategies (mirroring the S22+S23 sibling-
 * module pattern in `api/profileApi.ts`).
 *
 * Error response bodies are parsed once: text is read then `JSON.parse`d with
 * a try/catch, with the parsed value (or undefined on non-JSON) exposed via
 * `body`. This lets 412 callers inspect `{ expectedVersion, actualVersion,
 * currentState }` without consuming the stream twice.
 *
 * 204 No Content returns `{ data: undefined as T, etag: null, status: 204 }`
 * — the WageTypeMapping DELETE shape per S25 endpoint contract sets no ETag.
 *
 * S112 / TASK-11202 — typed structured overload: one path key can host
 * MULTIPLE verbs (`/api/admin/units/{id}` = PUT and DELETE), so the structured
 * call carries an explicit uppercase METHOD DISCRIMINANT:
 * `apiFetchWithEtag(pathKey, { method, params?, query?, ifMatch?, ifNoneMatch?, body? })`.
 * `ifMatch` takes the READY RFC 7232 wire string (compose via `lib/etag.ts`)
 * and is threaded as the `If-Match` header. The ETag/412 protocol (ADR-019)
 * is UNTOUCHED — the structured form merely normalizes into the same
 * (url, init) pair the legacy path consumes.
 *
 * S115 / TASK-11502 — the ADDITIVE `ifNoneMatch: '*'` option: the create-only
 * precondition (`If-None-Match: *`) used by the first-assign / create-row
 * writes (reporting-line assign, CHILD_SICK eligibility create). Verified
 * additive: no legacy `RequestInit` caller carries an `ifNoneMatch` key, so
 * the structured-vs-legacy discrimination cannot flip for any existing call.
 */

type EtagVerb = 'GET' | 'POST' | 'PUT' | 'DELETE'

/** Path keys in `Paths` exposing at least one typed operation on any verb. */
export type EtagPathIn<Paths> =
  | TypedPathIn<Paths, 'get'>
  | TypedPathIn<Paths, 'post'>
  | TypedPathIn<Paths, 'put'>
  | TypedPathIn<Paths, 'delete'>

/** The uppercase method discriminants valid for a path key (multi-verb aware). */
export type EtagMethodsIn<Paths, P> =
  | (P extends TypedPathIn<Paths, 'get'> ? 'GET' : never)
  | (P extends TypedPathIn<Paths, 'post'> ? 'POST' : never)
  | (P extends TypedPathIn<Paths, 'put'> ? 'PUT' : never)
  | (P extends TypedPathIn<Paths, 'delete'> ? 'DELETE' : never)

/** The structured options for a typed etag call — the method discriminant
    selects WHICH operation on the path key binds `params`/`query`/`body`. */
export type EtagOptionsIn<Paths, P extends keyof Paths, M extends EtagVerb> = {
  method: M
} & (
  | { ifMatch?: string; ifNoneMatch?: never }
  /** S115 — the create-only precondition. ONLY the literal `'*'` is admitted
      (RFC 7232 `If-None-Match: *` = "succeed only when no representation
      exists"); an entity-tag value here would be a different protocol.
      MUTUALLY EXCLUSIVE with `ifMatch` (Step-7a): a request carrying both
      preconditions has no single create-vs-update semantics. */
  | { ifMatch?: never; ifNoneMatch?: '*' }
) & StructuredOptionsForOp<OperationIn<Paths, Lowercase<M> & MethodKey, P>>

/** The success data for a typed etag call (same admission rule as the verbs). */
export type EtagDataIn<Paths, P extends keyof Paths, M extends EtagVerb> = SuccessDataOf<
  OperationIn<Paths, Lowercase<M> & MethodKey, P>
>

type EtagPath = EtagPathIn<paths>

/** The loose runtime shape of a structured etag call (hidden from callers). */
type EtagCallOptions = {
  method: string
  params?: { path?: Record<string, unknown> }
  query?: Record<string, unknown>
  ifMatch?: string
  ifNoneMatch?: string
  body?: unknown
}

// Runtime discrimination between the structured options and a legacy
// `RequestInit`. Behavior-preserving for every existing caller:
//  - any key outside {method, params, query, ifMatch, ifNoneMatch, body}
//    (e.g. `headers`) → legacy RequestInit;
//  - `params`/`query`/`ifMatch`/`ifNoneMatch` present → structured
//    (RequestInit has none — S115 grep-verified for `ifNoneMatch`);
//  - otherwise a plain-OBJECT `body` → structured (a RequestInit body is a
//    BodyInit — every existing caller pre-`JSON.stringify`s, so a STRING body
//    routes legacy and is sent exactly once, never double-stringified);
//  - `{ method }` alone routes legacy, where the two paths coincide (nothing
//    to interpolate, no body, no If-Match).
// LIMITATION (documented): a typed operation whose declared JSON body is a
// BARE STRING would misroute to the legacy path — no such op exists in the spec.
function isStructuredEtagCall(
  arg: RequestInit | EtagCallOptions | undefined,
): arg is EtagCallOptions {
  if (!arg || typeof arg !== 'object') return false
  const candidate = arg as EtagCallOptions
  if (typeof candidate.method !== 'string') return false
  const allowed = new Set(['method', 'params', 'query', 'ifMatch', 'ifNoneMatch', 'body'])
  if (!Object.keys(arg).every((k) => allowed.has(k))) return false
  if ('params' in arg || 'query' in arg || 'ifMatch' in arg || 'ifNoneMatch' in arg) return true
  return typeof candidate.body === 'object' && candidate.body !== null
}

// Typed structured overload (FIRST — same fallback-preservation rationale as
// the body verbs; additionally, every existing explicit-`T` caller supplies ONE
// type argument against this overload's TWO type parameters, which eliminates
// it as a candidate outright).
export function apiFetchWithEtag<P extends EtagPath, M extends EtagMethodsIn<paths, P>>(
  pathKey: P,
  options: EtagOptionsIn<paths, P, M>,
): Promise<ApiResult<ApiResponseWithEtag<EtagDataIn<paths, P, M>>>>
// Legacy overload (SECOND): the plain URL + RequestInit shape, byte-compatible.
export function apiFetchWithEtag<T>(
  url: string,
  init?: RequestInit,
): Promise<ApiResult<ApiResponseWithEtag<T>>>
export async function apiFetchWithEtag(
  urlOrKey: string,
  initOrOptions?: RequestInit | EtagCallOptions,
): Promise<ApiResult<ApiResponseWithEtag<unknown>>> {
  let url = urlOrKey
  let init: RequestInit | undefined
  if (isStructuredEtagCall(initOrOptions)) {
    // Normalize the structured form into the SAME (url, init) the legacy path
    // consumes — everything below this branch is protocol-identical (ADR-019).
    url = buildUrl('apiFetchWithEtag', urlOrKey, initOrOptions)
    // S115 — normalize the precondition options into the RFC 7232 headers the
    // legacy path has always consumed via `init.headers`. The two preconditions
    // are mutually exclusive (create-only vs update-only): the options type
    // rejects the combination and this guard backstops non-tsc callers.
    if (initOrOptions.ifMatch !== undefined && initOrOptions.ifNoneMatch !== undefined) {
      throw new Error(
        `apiFetchWithEtag: 'ifMatch' and 'ifNoneMatch' are mutually exclusive preconditions for '${urlOrKey}' — pass exactly one`,
      )
    }
    const preconditionHeaders: Record<string, string> = {}
    if (initOrOptions.ifMatch !== undefined) {
      preconditionHeaders['If-Match'] = initOrOptions.ifMatch
    }
    if (initOrOptions.ifNoneMatch !== undefined) {
      preconditionHeaders['If-None-Match'] = initOrOptions.ifNoneMatch
    }
    init = {
      method: initOrOptions.method,
      headers: Object.keys(preconditionHeaders).length > 0 ? preconditionHeaders : undefined,
      body: initOrOptions.body !== undefined ? JSON.stringify(initOrOptions.body) : undefined,
    }
  } else {
    init = initOrOptions as RequestInit | undefined
  }

  const token = getToken()
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }
  // Allow callers to pass `If-Match` / `If-None-Match` (or any other header)
  // via `init.headers`. Caller-supplied entries override our defaults.
  if (init?.headers) {
    const incoming = init.headers
    if (incoming instanceof Headers) {
      incoming.forEach((v, k) => { headers[k] = v })
    } else if (Array.isArray(incoming)) {
      for (const [k, v] of incoming) headers[k] = v
    } else {
      for (const [k, v] of Object.entries(incoming as Record<string, string>)) {
        headers[k] = v
      }
    }
  }

  try {
    const res = await fetch(url, {
      ...init,
      headers,
    })

    if (res.status === 401) {
      handle401()
      return { ok: false, error: 'Unauthorized', status: 401 }
    }

    if (!res.ok) {
      // Read body text once. Try JSON; fall back to text for the error message.
      let bodyText = ''
      try {
        bodyText = await res.text()
      } catch {
        // ignore
      }
      let parsed: unknown
      if (bodyText) {
        try {
          parsed = JSON.parse(bodyText)
        } catch {
          parsed = undefined
        }
      }
      const errorMessage = bodyText || `HTTP ${res.status}`
      return { ok: false, error: errorMessage, status: res.status, body: parsed }
    }

    // 204 No Content — no body, no ETag header expected (e.g. WageTypeMapping DELETE).
    if (res.status === 204) {
      return {
        ok: true,
        data: { data: undefined, etag: null, status: 204 },
      }
    }

    const etag = res.headers.get('ETag')
    const data = (await res.json()) as unknown
    return {
      ok: true,
      data: { data, etag, status: res.status },
    }
  } catch (e) {
    return { ok: false, error: String(e), status: 0 }
  }
}

// ════════════════════════════════════════════════════════════════════════════
// S72 / TASK-7204 — typed Skema row-preferences PUT (SPRINT-72 R4/R13).
// The wire contract is TASK-7201's: FULL replacement, sortOrder dense 0..n-1 in
// submitted order; 200 returns the new effective `rowPreferences` shape; 422
// returns the `row_preferences_invalid` offender payload; 403 = self-only.
// ════════════════════════════════════════════════════════════════════════════

/** PUT /api/skema/{employeeId}/row-preferences body (7201 contract, verbatim
    field names — note the absence entries use `absenceType`, not `type`). */
export interface SkemaRowPreferencesPutBody {
  projects: { projectId: string; sortOrder: number }[]
  absenceTypes: { absenceType: string; sortOrder: number }[]
}

/** The 7201 422 payload — preserved typed so the manager modal can render the
    offenders (SPRINT-72 TASK-7204 acceptance). */
export interface SkemaRowPreferencesInvalidPayload {
  error: 'row_preferences_invalid'
  invalidProjectIds: string[]
  invalidAbsenceTypes: string[]
  duplicateProjectIds: string[]
  duplicateAbsenceTypes: string[]
  message: string
}

/** Result of `putSkemaRowPreferences` — the `ApiResult` convention (non-throwing)
    extended with the parsed 422 payload when the server rejected the replacement. */
export type PutSkemaRowPreferencesResult =
  | { ok: true; data: SkemaRowPreferences }
  | { ok: false; status: number; error: string; invalid?: SkemaRowPreferencesInvalidPayload }

/** Derives the exact PUT body from ordered selections: sortOrder = the ARRAY
    INDEX (dense 0..n-1 by construction — any stale/sparse `sortOrder` carried on
    the entries is deliberately ignored; the submitted ORDER is authoritative).
    This is the single owner of the modal's "dense renumbering on save" rule. */
export function toRowPreferencesPutBody(
  projects: readonly SkemaRowPreferenceProject[],
  absenceTypes: readonly SkemaRowPreferenceAbsenceType[],
): SkemaRowPreferencesPutBody {
  return {
    projects: projects.map((p, i) => ({ projectId: p.projectId, sortOrder: i })),
    absenceTypes: absenceTypes.map((a, i) => ({ absenceType: a.type, sortOrder: i })),
  }
}

/**
 * Typed row-preferences replacement (SPRINT-72 R4) via the standard `apiClient`
 * (token injection + 401 handling inherited).
 *
 * R16 sequencing contract (for the 7205 page wiring): this function performs
 * EXACTLY ONE PUT and resolves only after the server has applied the full
 * replacement — it never refetches and has no side effects on caches. The page
 * must therefore sequence: (1) flush pending debounced cell/workTime saves,
 * (2) `await putSkemaRowPreferences(...)`, (3) refetch the month — so the
 * refetch can never clobber in-flight local state.
 *
 * On 422 the raw error text is parsed and, when it is the 7201
 * `row_preferences_invalid` shape, exposed typed via `invalid` so the modal can
 * list the offenders. Other failures (403 self-only, 5xx, network) return the
 * plain `ApiResult`-style error variant without `invalid`.
 */
export async function putSkemaRowPreferences(
  employeeId: string,
  body: SkemaRowPreferencesPutBody,
): Promise<PutSkemaRowPreferencesResult> {
  const result = await apiClient.put<SkemaRowPreferences>(
    `/api/skema/${employeeId}/row-preferences`,
    body,
  )
  if (result.ok) {
    return { ok: true, data: result.data }
  }
  let invalid: SkemaRowPreferencesInvalidPayload | undefined
  if (result.status === 422) {
    try {
      const parsed = JSON.parse(result.error) as SkemaRowPreferencesInvalidPayload
      if (parsed && parsed.error === 'row_preferences_invalid') {
        invalid = parsed
      }
    } catch {
      // Non-JSON 422 body — fall through with the raw error text only.
    }
  }
  return { ok: false, status: result.status, error: result.error, invalid }
}
