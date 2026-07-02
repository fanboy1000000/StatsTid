// S76b / TASK-7602 — shared employee-profile read/write helpers for the unified
// EditPersonDrawer. Extracted from the inline helpers in `UserManagement.tsx`
// (S53 TASK-5306e).
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the `enhedLabel` field was REMOVED
// from the employee-profile GET/PUT DTO; this module no longer reads or sends it.
//
// S112 / TASK-11203 — both calls switched to the TYPED `apiFetchWithEtag(pathKey,
// { method, params, ifMatch?, body? })` overload (PAT-012): the response type is
// the spec `EmployeeProfileResponse` (re-narrowed via `coerceApiResponse`, drift-
// guarded below) and the PUT body is compile-checked against the spec
// `UpdateEmployeeProfileRequest`. Two hand-written-type lies fell out of the
// switch: the wire interface claimed a `weeklyNormHours` response field the
// backend does not serve, and the PUT sent a `weeklyNormHours: 0` placeholder the
// backend request DTO no longer declares — both dropped.
//
// `UserManagement.tsx` was retired (S109); this module is the single source of
// truth for the drawer.
import { apiFetchWithEtag } from '../../../lib/api'
import { coerceApiResponse, type Assert, type AssertFieldsInSpec } from '../../../lib/apiNarrow'
import { formatVersionAsIfMatch, resolveEtag } from '../../../lib/etag'

/** Snapshot of an employee_profiles row + the row-version concurrency token. */
export interface EmployeeProfileSnapshot {
  employeeId: string
  partTimeFraction: number
  position: string | null
  isPartTime: boolean
  version: number
  etag: string
}

interface EmployeeProfileWire {
  employeeId: string
  partTimeFraction: number
  position: string | null
  isPartTime: boolean
  version: number
}

// S112 — compile-time drift guard: every field the FE reads must exist in the
// spec `EmployeeProfileResponse` (the S97→S100 drift class, caught at build).
export type _EmployeeProfileDrift = Assert<
  AssertFieldsInSpec<EmployeeProfileWire, 'StatsTid.Backend.Api.Contracts.EmployeeProfileResponse'>
>

function toSnapshot(data: EmployeeProfileWire, etag: string | null): EmployeeProfileSnapshot {
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    employeeId: data.employeeId,
    partTimeFraction: data.partTimeFraction,
    position: data.position,
    isPartTime: data.isPartTime,
    version: data.version,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
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
  return toSnapshot(coerceApiResponse<EmployeeProfileWire>(data), etag)
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
  return toSnapshot(coerceApiResponse<EmployeeProfileWire>(data), etag)
}
