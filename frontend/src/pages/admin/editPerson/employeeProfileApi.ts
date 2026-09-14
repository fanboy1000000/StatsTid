// S76b / TASK-7602 — shared employee-profile read/write helpers for the unified
// EditPersonDrawer. Extracted from the inline helpers in `UserManagement.tsx`
// (S53 TASK-5306e).
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the `enhedLabel` field was REMOVED
// from the employee-profile GET/PUT DTO; this module no longer reads or sends it.
//
// S112 / TASK-11203 — both calls switched to the TYPED `apiFetchWithEtag(pathKey,
// { method, params, ifMatch?, body? })` overload (PAT-012): the response type is
// the spec `EmployeeProfileResponse` and the PUT body is compile-checked against
// the spec `UpdateEmployeeProfileRequest`. Two hand-written-type lies fell out of
// the switch: the wire interface claimed a `weeklyNormHours` response field the
// backend does not serve, and the PUT sent a `weeklyNormHours: 0` placeholder the
// backend request DTO no longer declares — both dropped.
//
// S113 / TASK-11301 — the generated spec type is STRICT, so the hand-written wire
// interface + the S111 coercion re-narrowing (the deleted apiNarrow module) are
// gone: `toSnapshot` consumes the spec `EmployeeProfileResponse` directly.
//
// `UserManagement.tsx` was retired (S109); this module is the single source of
// truth for the drawer.
//
// S141 / TASK-14107 (B0 — the owner's visibility requirement: "should it not
// be visible to an HR employee that another has scheduled a change?") — the
// GET/PUT response now carries `scheduled`: the next dated profile row after
// today, or an explicit `null` when nothing is scheduled. The drawer reads
// this straight off the SAME payload (no second call — B0 forbids that bolt-on
// shape). S141 / OQ-3 (a) also moves this row's concurrency token: `version` /
// the ETag header are now the EMPLOYEE's one aggregate token (`users.version`),
// not this row's own — see `useEditPerson.saveEdit`'s comment on the running
// version cursor for why every write AFTER this one in the drawer's save
// sequence depends on that. OQ-6 (a): the PUT body carries the caller's
// `carryForwardToScheduledChange` choice (meaningful only when `scheduled` was
// non-null at read time; the backend diffs the sent values against today's and
// carries forward only the field(s) that actually differ).
import { apiFetchWithEtag } from '../../../lib/api'
import type { components } from '../../../lib/api-types'
import { formatVersionAsIfMatch, resolveEtag } from '../../../lib/etag'

/** The next dated employee-profile row after today (S141 B0) — `null` when
    nothing is scheduled. The GENERATED spec type verbatim. */
export type ScheduledProfileChange =
  components['schemas']['StatsTid.Backend.Api.Contracts.ScheduledProfileChange']

/** Snapshot of an employee_profiles row + the row-version concurrency token
    (S141: the token is `users.version`, the employee's ONE aggregate token —
    see the file header). */
export interface EmployeeProfileSnapshot {
  employeeId: string
  partTimeFraction: number
  position: string | null
  isPartTime: boolean
  version: number
  etag: string
  scheduled: ScheduledProfileChange | null
}

/** The GET/PUT response — the GENERATED spec type verbatim (S113). */
type EmployeeProfileWire = components['schemas']['StatsTid.Backend.Api.Contracts.EmployeeProfileResponse']

function toSnapshot(data: EmployeeProfileWire, etag: string | null): EmployeeProfileSnapshot {
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    employeeId: data.employeeId,
    partTimeFraction: data.partTimeFraction,
    position: data.position,
    isPartTime: data.isPartTime,
    version: data.version,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
    scheduled: data.scheduled,
  }
}

/** HR-only GET. Returns null when no live profile row exists (404). */
export async function fetchEmployeeProfile(
  employeeId: string,
): Promise<EmployeeProfileSnapshot | null> {
  const result = await apiFetchWithEtag('/api/admin/employee-profiles/{employeeId}', {
    method: 'GET',
    params: { path: { employeeId } },
  })
  if (!result.ok) return null
  const { data, etag } = result.data
  return toSnapshot(data, etag)
}

/**
 * HR-only PUT (admin-strict If-Match → 412 stale / 428 missing). Throws a
 * status-tagged Error on failure so the save orchestrator can branch on 412 vs
 * other.
 */
export async function saveEmployeeProfile(
  employeeId: string,
  ifMatch: string,
  body: {
    effectiveFrom: string
    partTimeFraction: number
    position: string | null
    /** S141 / OQ-6 (a) — set ONLY when a scheduled change exists and HR chose
        to carry the edit into it too; omitted/undefined = the default "apply
        until the scheduled change" behaviour. */
    carryForwardToScheduledChange?: boolean
  },
): Promise<EmployeeProfileSnapshot> {
  const result = await apiFetchWithEtag('/api/admin/employee-profiles/{employeeId}', {
    method: 'PUT',
    params: { path: { employeeId } },
    ifMatch,
    body,
  })
  if (!result.ok) {
    // Object.assign (not an `as` cast) — this file is on the S112 no-`as` surface.
    throw Object.assign(new Error(result.error), { status: result.status, body: result.body })
  }
  const { data, etag } = result.data
  return toSnapshot(data, etag)
}
