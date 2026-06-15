// S76b / TASK-7602 — shared employee-profile read/write helpers for the unified
// EditPersonDrawer. Extracted from the inline helpers in `UserManagement.tsx`
// (S53 TASK-5306e) and extended with the S74 `enhedLabel` free-text display
// label on the PUT body (the new field the unified editor must surface). The
// backend GET serves `enhedLabel` (EmployeeProfileEndpoints.cs:145) and the PUT
// accepts it (UpdateEmployeeProfileRequest.EnhedLabel) — so the drawer threads
// it through exactly like `position`.
//
// `UserManagement.tsx` keeps its own private copies (7604 retires that surface);
// this module is the single source of truth for the drawer.
import { apiFetchWithEtag } from '../../../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../../../lib/etag'

/** Snapshot of an employee_profiles row + the row-version concurrency token. */
export interface EmployeeProfileSnapshot {
  employeeId: string
  partTimeFraction: number
  position: string | null
  // S74 / TASK-7400 — free-text "enhed" display label (additive, display-only;
  // null when unset → the FE falls back to the primary-org name elsewhere).
  enhedLabel: string | null
  isPartTime: boolean
  version: number
  etag: string
}

interface EmployeeProfileWire {
  employeeId: string
  weeklyNormHours: number
  partTimeFraction: number
  position: string | null
  enhedLabel: string | null
  isPartTime: boolean
  version: number
}

function toSnapshot(data: EmployeeProfileWire, etag: string | null): EmployeeProfileSnapshot {
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    employeeId: data.employeeId,
    partTimeFraction: data.partTimeFraction,
    position: data.position,
    enhedLabel: data.enhedLabel ?? null,
    isPartTime: data.isPartTime,
    version: data.version,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
  }
}

/** HR-only GET. Returns null when no live profile row exists (404). */
export async function fetchEmployeeProfile(
  employeeId: string,
): Promise<EmployeeProfileSnapshot | null> {
  const result = await apiFetchWithEtag<EmployeeProfileWire>(
    `/api/admin/employee-profiles/${encodeURIComponent(employeeId)}`,
  )
  if (!result.ok) return null
  const { data, etag } = result.data
  return toSnapshot(data, etag)
}

/**
 * HR-only PUT (admin-strict If-Match → 412 stale / 428 missing). The backend
 * DTO still requires `weeklyNormHours` (being phased out) — send 0 as a
 * placeholder; the backend ignores it for domain logic. `enhedLabel` threads
 * through like `position`. Throws a status-tagged Error on failure so the save
 * orchestrator can branch on 412 vs other.
 */
export async function saveEmployeeProfile(
  employeeId: string,
  ifMatch: string,
  body: {
    effectiveFrom: string
    partTimeFraction: number
    position: string | null
    enhedLabel: string | null
  },
): Promise<EmployeeProfileSnapshot> {
  const wireBody = {
    effectiveFrom: body.effectiveFrom,
    weeklyNormHours: 0,
    partTimeFraction: body.partTimeFraction,
    position: body.position,
    enhedLabel: body.enhedLabel,
  }
  const result = await apiFetchWithEtag<EmployeeProfileWire>(
    `/api/admin/employee-profiles/${encodeURIComponent(employeeId)}`,
    {
      method: 'PUT',
      headers: { 'If-Match': ifMatch },
      body: JSON.stringify(wireBody),
    },
  )
  if (!result.ok) {
    const err = new Error(result.error) as Error & { status: number; body?: unknown }
    err.status = result.status
    err.body = result.body
    throw err
  }
  const { data, etag } = result.data
  return toSnapshot(data, etag)
}
