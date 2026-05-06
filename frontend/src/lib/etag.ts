// ETag wire-format helpers (S22 / ADR-018 D7).
//
// Background: the local-agreement-profile concurrency token used to be the
// profile UUID, transmitted as a quoted opaque ETag (`ETag: "11111111-..."`,
// `If-Match: "11111111-..."`). ADR-018 D7 (TASK-2205/2207) replaces the
// profile-id-as-ETag with a numeric `version` column on the profile row, and
// the wire format becomes a quoted decimal integer (e.g. `ETag: "5"`,
// `If-Match: "5"`). Quoting follows the RFC 7232 entity-tag convention
// (`opaque-tag = DQUOTE *etagc DQUOTE`).
//
// These helpers exist so the parsing/formatting rule lives in exactly one
// place — both `profileApi.ts` (raw fetch wiring) and `useConfig.ts`
// (React hooks) call into them rather than reaching into the wire string.

/**
 * Parse a numeric profile version out of a server-returned ETag header.
 *
 * Accepts the canonical RFC 7232 quoted form (`"5"`) and the unquoted form
 * (`5`). Returns `null` for null/missing headers, empty bodies, or
 * unparseable content. Per ADR-018 D7 the version is a positive integer; we
 * still hand back any safe parsed integer so callers don't have to special-
 * case zero (the backend never emits zero, but tests and migration paths may).
 *
 * @param etagHeader Raw value from `Response.headers.get('ETag')`, or null.
 * @returns Parsed version, or null if the input is absent / malformed.
 */
export function parseVersionFromETag(etagHeader: string | null): number | null {
  if (etagHeader === null) return null
  const trimmed = etagHeader.trim()
  if (trimmed.length === 0) return null
  // Strip exactly one pair of surrounding double-quotes if present.
  const unquoted = trimmed.startsWith('"') && trimmed.endsWith('"')
    ? trimmed.slice(1, -1)
    : trimmed
  if (unquoted.length === 0) return null
  // Reject anything that isn't a clean integer literal — RFC 7232 disallows
  // weak validators here (`W/"5"`); strict integer parse keeps the contract
  // narrow.
  if (!/^-?\d+$/.test(unquoted)) return null
  const parsed = Number.parseInt(unquoted, 10)
  if (!Number.isFinite(parsed) || !Number.isSafeInteger(parsed)) return null
  return parsed
}

/**
 * Format a numeric profile version as an outgoing If-Match header value.
 *
 * Returns the RFC 7232 quoted form (e.g. `"5"`). The backend's
 * `ConfigEndpoints.TryParseConcurrencyPrecondition` strips the quotes before
 * parsing, but the canonical wire form is quoted and we always emit it that
 * way.
 *
 * @param version Numeric version returned by an earlier GET / PUT.
 * @returns RFC 7232 quoted ETag suitable for an `If-Match` header.
 */
export function formatVersionAsIfMatch(version: number): string {
  return `"${version}"`
}

/**
 * Resolve the canonical (etag, version) pair for a profile response.
 *
 * Prefers the `ETag` response header. Falls back to the response body's
 * `version` field when the header is missing or unparseable — the typical
 * cross-origin case where `Access-Control-Expose-Headers: ETag` was not set
 * (S22 Step 7a Codex cycle-3 P2; resolved in S23 / TASK-2303).
 *
 * Body-side validation per S23 Step 0b plan-mode review (Codex Item 3
 * WARNING): a malformed body could carry `version: undefined` / `null` /
 * `"5"` / `NaN` / `0`. Synthesizing `"undefined"` into an If-Match header
 * would push a bogus optimistic-concurrency token to the backend. Instead
 * we strict-validate (`typeof === 'number' && Number.isSafeInteger && >= 1`)
 * and return `{ etag: null, version: null }` on validation failure so the
 * caller surfaces a proper error path instead of a manufactured token.
 *
 * @param headerValue Raw `Response.headers.get('ETag')` result.
 * @param body Decoded response body (or null if unavailable).
 * @returns Canonical ETag wire form + parsed version, or both null.
 */
export function resolveEtag(
  headerValue: string | null,
  body: { version?: unknown } | null | undefined,
): { etag: string | null; version: number | null } {
  const headerVersion = parseVersionFromETag(headerValue)
  if (headerVersion !== null) {
    return { etag: headerValue, version: headerVersion }
  }
  const bodyVersion = body?.version
  if (
    typeof bodyVersion === 'number' &&
    Number.isSafeInteger(bodyVersion) &&
    bodyVersion >= 1
  ) {
    return { etag: `"${bodyVersion}"`, version: bodyVersion }
  }
  return { etag: null, version: null }
}
