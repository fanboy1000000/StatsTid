import type {
  SkemaRowPreferenceAbsenceType,
  SkemaRowPreferenceProject,
  SkemaRowPreferences,
} from '../types'

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

export const apiClient = {
  get<T>(path: string): Promise<ApiResult<T>> {
    return request<T>('GET', path)
  },
  post<T>(path: string, body?: unknown): Promise<ApiResult<T>> {
    return request<T>('POST', path, body)
  },
  put<T>(path: string, body?: unknown): Promise<ApiResult<T>> {
    return request<T>('PUT', path, body)
  },
  delete<T>(path: string): Promise<ApiResult<T>> {
    return request<T>('DELETE', path)
  },
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
 */
export async function apiFetchWithEtag<T>(
  url: string,
  init?: RequestInit,
): Promise<ApiResult<ApiResponseWithEtag<T>>> {
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
        data: { data: undefined as T, etag: null, status: 204 },
      }
    }

    const etag = res.headers.get('ETag')
    const data = (await res.json()) as T
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
