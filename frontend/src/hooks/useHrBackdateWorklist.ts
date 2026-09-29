// SPRINT-140 / TASK-14007 (refinement B5) — the HR backdate diagnostic worklist
// (S138 / TASK-13803, PAT-026, ADR-013). Two operations, both HROrAbove:
//  - GET  /api/hr/backdate-worklist            — the open (unresolved) rows,
//    org-wide (no `employeeId` query — the org-scoped listing per
//    `BackdateWorklistEndpoints.cs`; `open` defaults true server-side).
//  - POST /api/hr/backdate-worklist/{worklistId}/resolve — the ONE write this
//    task builds: RECALCULATED | DISMISSED | HANDLED_MANUALLY + a reason,
//    admin-strict If-Match on the row's `version` (428 missing/malformed
//    header, 412 stale, 409 already-resolved OR recalc-blocked, 403 not
//    permitted for this kind/role — all surfaced to the caller via the typed
//    ApiResult, never swallowed; the structured error body — `kind`,
//    `blockedBy` — rides on `ApiResult.body`, which this hook never drops).
//
// Both calls ride the GENERATED spec-keyed typed client (PAT-012) — no
// hand-written wire shapes.
import { useCallback } from 'react'
import { apiClient, apiFetchWithEtag, type ApiResult, type ApiResponseWithEtag } from '../lib/api'
import { formatVersionAsIfMatch } from '../lib/etag'
import type { components } from '../lib/api-types'

export type BackdateWorklistRow =
  components['schemas']['StatsTid.Backend.Api.Contracts.BackdateWorklistRow']
export type BackdateWorklistTriggerDto =
  components['schemas']['StatsTid.Backend.Api.Contracts.BackdateWorklistTriggerDto']
export type BackdateWorklistResolveResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.BackdateWorklistResolveResponse']

/** Wire values of `ResolveBackdateWorklistRequest.resolution` (`HrBackdateWorklistRepository.WorklistResolutions`). */
export const WorklistResolutions = {
  Recalculated: 'RECALCULATED',
  Dismissed: 'DISMISSED',
  HandledManually: 'HANDLED_MANUALLY',
} as const
export type WorklistResolution = (typeof WorklistResolutions)[keyof typeof WorklistResolutions]

/** Wire values of `BackdateWorklistRow.kind`. */
export const WorklistKinds = {
  ExportedMonth: 'EXPORTED_MONTH',
  SettledYear: 'SETTLED_YEAR',
} as const

export function useHrBackdateWorklist() {
  /** Org-wide open rows (the actor's HR-floored accessible-org set; 403 on empty scope). */
  const fetchWorklist = useCallback(async (): Promise<ApiResult<BackdateWorklistRow[]>> => {
    return apiClient.get('/api/hr/backdate-worklist')
  }, [])

  /**
   * Resolve one row. `version` is the row's `BackdateWorklistRow.version` (also the ETag the
   * GET/resolve responses carry) — composed here as the admin-strict `If-Match` the endpoint
   * requires. Callers branch on `result.status`: 412 = stale (refetch + inform), 428 = this
   * hook failed to send the header (a client bug — never expected, never silently retried),
   * 409 = TWO kinds, told apart by `result.body.kind`: `"worklist-recalc-blocked"` (a RECALCULATED
   * attempt on a blocked row; `body.blockedBy` names the blocking ids) versus a body with no
   * `kind` (already resolved by someone else); 403 = this role may not use that verb on that
   * row kind. The `ApiResult` is returned untouched — never narrowed or re-wrapped.
   */
  const resolveWorklistItem = useCallback(
    async (
      worklistId: string,
      version: number,
      resolution: WorklistResolution,
      reason: string,
    ): Promise<ApiResult<ApiResponseWithEtag<BackdateWorklistResolveResponse>>> => {
      return apiFetchWithEtag('/api/hr/backdate-worklist/{worklistId}/resolve', {
        method: 'POST',
        params: { path: { worklistId } },
        ifMatch: formatVersionAsIfMatch(version),
        body: { resolution, reason },
      })
    },
    [],
  )

  return { fetchWorklist, resolveWorklistItem }
}
