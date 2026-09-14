// S141 / TASK-14109 (refinement C2) — the read hook for the employment history
// screen, over the TASK-14113 endpoint `GET /api/hr/employees/{employeeId}/history`.
//
// WHAT THIS ANSWERS. HR cannot currently answer "what has changed for this
// employee, and when did it take effect?" — the audit log has no subject
// filter and orders by when a change was RECORDED, not when it took EFFECT.
// This hook is a thin wrapper: it does not compute anything the server did
// not already compute (statuses, "what changed", track separation are all
// server-side) — it fetches, exposes the typed envelope, and reports the
// STATUS CODE alongside the error string so the screen can tell a genuine
// refusal (422 — a malformed `from`/`to` window) from an access denial (403 —
// "unknown employee" and "not in your scope" are DELIBERATELY
// indistinguishable server-side, so this hook does not try to guess either)
// from an unexpected failure.
//
// Rides the GENERATED spec-keyed typed client (PAT-012) — no hand-written
// wire shape. The endpoint declares only its 200 in the committed OpenAPI
// spec (`docs/api/openapi.json`); a 403/422 still comes back through the
// SAME `ApiResult` `ok:false` branch (untyped `status`/`error`, exactly like
// every other typed GET's failure path — see `useBalanceSummary.ts`).
import { useCallback, useEffect, useRef, useState } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

export type EmploymentHistoryResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.EmploymentHistoryResponse']
export type EmploymentProfileHistoryInterval =
  components['schemas']['StatsTid.Backend.Api.Contracts.EmploymentProfileHistoryInterval']
export type AgreementCodeHistoryInterval =
  components['schemas']['StatsTid.Backend.Api.Contracts.AgreementCodeHistoryInterval']

/** The closed status vocabulary the endpoint serves (`EmploymentHistoryIntervalStatus`,
    `EmploymentHistoryEndpoints.cs`). Kept as a literal union here rather than trusting the
    generated `status: string` so a rendering `switch` is exhaustive-checked by `tsc`. */
export type HistoryIntervalStatus = 'PAST' | 'CURRENT' | 'SCHEDULED'

/**
 * Fetches the employee's history over `[from, to)` (both optional — omitted means unbounded).
 * Does NOT fetch until `employeeId` is non-null (the bare "pick an employee" screen renders
 * with `employeeId: null` and no request goes out).
 *
 * `errorStatus` is exposed alongside `error` so the screen can distinguish:
 *  - 422 — the window itself is malformed (`from >= to`): a REFUSAL, not "no history";
 *  - 403 — access denied, which also covers "no such employee" by the endpoint's own design
 *    (an unknown id must not read differently from an out-of-scope one, or the response itself
 *    would be an existence oracle);
 *  - anything else — an unexpected failure.
 */
export function useEmploymentHistory(employeeId: string | null, from?: string, to?: string) {
  const [data, setData] = useState<EmploymentHistoryResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [errorStatus, setErrorStatus] = useState<number | null>(null)

  // S126/F2-style stale-response guard (the same class this codebase already pins in
  // useBalanceSummary/useYearOverview/AuditLogView): a from/to edit can fire a second request
  // before the first resolves, and an older response landing last must not clobber the answer
  // to the filter the user is actually looking at now.
  const latestRequestId = useRef(0)

  const fetchHistory = useCallback(() => {
    if (!employeeId) return
    const requestId = ++latestRequestId.current
    setLoading(true)
    setError(null)
    setErrorStatus(null)
    void apiClient
      .get('/api/hr/employees/{employeeId}/history', {
        params: { path: { employeeId } },
        query: { from: from || undefined, to: to || undefined },
      })
      .then((result) => {
        if (requestId !== latestRequestId.current) return
        if (result.ok) {
          setData(result.data)
        } else {
          setData(null)
          setError(result.error)
          setErrorStatus(result.status)
        }
        setLoading(false)
      })
  }, [employeeId, from, to])

  useEffect(() => {
    fetchHistory()
  }, [fetchHistory])

  return { data, loading, error, errorStatus, refetch: fetchHistory }
}
