// S86 / TASK-8601 — the shared "resolve the active PRIMARY reporting line + its
// ETag" helper. The drawer path resolves this inline in LifecycleSections
// (fetchEmployeeLines → pick the active PRIMARY line → `"${primary.version}"` as
// the If-Match token). The NEW inline row affordances must replicate that resolve
// BEFORE an assign/reassign — the roster row carries `structuralApproverId` but
// NO line version, so assigning from roster state would silently become an
// If-Match-less second save path (the Step-0b ETag-hydration WARNING). This hook
// is that single resolve, shared so the row and the drawer hit the SAME If-Match
// semantics (first assign → If-None-Match:*; reassign → If-Match the line ETag).
import { useCallback, useState } from 'react'
import { useReportingLines } from '../../../hooks/useReportingLines'

export interface ResolvedReportingLine {
  /** The active PRIMARY manager id, or null when none (a first-assign case). */
  approverId: string | null
  /** `"${version}"` of the active PRIMARY line, or null when none. The If-Match
      token for a reassign/remove; null ⇒ a FIRST assign (If-None-Match:*). */
  etag: string | null
}

/**
 * Resolve the active PRIMARY reporting line for `employeeId` on demand. Returns a
 * `resolve` callback (idempotent; re-resolves on each call so a follow-up action
 * uses the fresh version) + the last-resolved value + a `resolving` flag. The
 * lazy-mounted inline controller calls `resolve()` before opening the picker so
 * the assign carries the correct If-Match.
 */
export function useResolveReportingLine() {
  const { fetchEmployeeLines } = useReportingLines()
  const [resolving, setResolving] = useState(false)
  const [resolved, setResolved] = useState<ResolvedReportingLine | null>(null)

  const resolve = useCallback(
    async (employeeId: string): Promise<ResolvedReportingLine | null> => {
      setResolving(true)
      try {
        const lines = await fetchEmployeeLines(employeeId)
        if (!lines.ok) {
          // Couldn't resolve → return null; the caller treats it as "no line yet"
          // (a first-assign attempt) and the server's If-None-Match:* / 409 is the
          // authoritative guard (never assign from a stale roster version).
          const fallback: ResolvedReportingLine = { approverId: null, etag: null }
          setResolved(fallback)
          return fallback
        }
        const primary = lines.data.active.find((l) => l.relationship === 'PRIMARY')
        const next: ResolvedReportingLine = {
          approverId: primary?.managerId ?? null,
          etag: primary ? `"${primary.version}"` : null,
        }
        setResolved(next)
        return next
      } finally {
        setResolving(false)
      }
    },
    [fetchEmployeeLines],
  )

  return { resolve, resolved, resolving }
}
