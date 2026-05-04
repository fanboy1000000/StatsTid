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
