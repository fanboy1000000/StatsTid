// SPRINT-141 / TASK-14108 (C1, refinement section on the termination screen) — the data layer
// for "record that an employee is leaving". This is the ONE write on the screen: PUT
// /api/admin/employees/{employeeId}/employment-end-date (S70/S71/S136 — pre-existing, not new
// this sprint). Four reads support it:
//   - the terminated-inclusive employment-end-date GET (REQUIRED) — the only read that still
//     answers for an employee who is ALREADY terminated, and therefore the ONLY source of the
//     concurrency token this screen's PUT sends as If-Match;
//   - the employment-start-date GET (best-effort, for the "hired since" context) — this one is
//     ACTIVE-ONLY (`ValidateEmployeeAccessAsync` / `GetByIdWithVersionAsync` both filter
//     `is_active = TRUE`, `EmploymentDateEndpoints.cs:83/87`), so it 404s for an employee this
//     screen is revisiting AFTER termination. Failure here is swallowed, not surfaced;
//   - the employee-profile GET (best-effort but terminated-inclusive) — carries the B0 "next
//     scheduled change" for the profile fields (fraction/position/category), which this screen
//     must show per the owner's standing S141 requirement: a scheduled change must be visible
//     wherever a profile value is shown, and this screen shows profile values;
//   - the user-detail GET (best-effort, for a display name) — ALSO active-only
//     (`GetByIdWithVersionAsync(userId, ct)`, `UserRepository.cs:151`), so it 404s post-
//     termination too. When it succeeds it ALSO carries a second B0 field —
//     `scheduledAgreementCode` — a scheduled agreement-code change, which this screen surfaces
//     alongside the profile one rather than silently dropping.
//
// ★ Two of the PUT's refusal shapes are UNTYPED anonymous objects BY DELIBERATE BACKEND DECISION
// (`EmploymentDateEndpoints.cs:644-646`: "the 409 settlement-conflict body stays UNTYPED —
// error-shape typing is explicitly out of the Pass-2 scope"). The generated contract therefore
// gives NO help distinguishing them — in fact NO non-200 response is declared for this operation
// at all (`Produces<EmploymentEndDateResponse>(200)` is the only annotation). Both are
// hand-written below from the C# source (`:560-591` the settlement conflict, `:801-827` the
// strand guard) and pinned in `__tests__/useTermination.test.ts` — if the server shape ever
// drifts, that test is the only thing that will notice.
import { useCallback } from 'react'
import { apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'
import type { components } from '../lib/api-types'

export type EmploymentEndDateResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.EmploymentEndDateResponse']
export type EmploymentStartDateResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.EmploymentStartDateResponse']
export type EmployeeProfileResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.EmployeeProfileResponse']
export type ScheduledProfileChange =
  components['schemas']['StatsTid.Backend.Api.Contracts.ScheduledProfileChange']
export type UserDetailResponse =
  components['schemas']['StatsTid.Backend.Api.Contracts.UserDetailResponse']
export type ScheduledAgreementCodeChangeDto =
  components['schemas']['StatsTid.Backend.Api.Contracts.ScheduledAgreementCodeChangeDto']

/** The terminated-inclusive employment-end-date read, plus the resolved concurrency token
    (`etag`) this screen's PUT must send as `If-Match` — the ONLY token source that still
    answers once an employee is already terminated (`EmploymentDateEndpoints.cs:369-371`). */
export interface EmploymentEndDateSnapshot {
  employeeId: string
  employmentEndDate: string | null
  endDateDeactivated: boolean
  isActive: boolean
  version: number
  etag: string
}

function toEndDateSnapshot(
  data: EmploymentEndDateResponse,
  etagHeader: string | null,
): EmploymentEndDateSnapshot {
  const { etag } = resolveEtag(etagHeader, data)
  return {
    employeeId: data.employeeId,
    employmentEndDate: data.employmentEndDate,
    endDateDeactivated: data.endDateDeactivated,
    isActive: data.isActive,
    version: data.version,
    etag: etag ?? formatVersionAsIfMatch(data.version),
  }
}

// ════════════════════════════════════════════════════════════════════════════
// Hand-written refusal shapes (NOT in the generated contract — see the file
// banner). Each carries a narrow type-guard keyed on a field unique to that
// shape, so a caller can safely discriminate `result.body: unknown`.
// ════════════════════════════════════════════════════════════════════════════

/** 409 — an active (non-REVERSED) vacation settlement exists for a ferieår the proposed end-date
    change would affect (SPRINT-70 R7a / SPRINT-71 R13). Named after what HR must do about it, not
    after the HTTP shape: reverse the settlement first. `EmploymentDateEndpoints.cs:560-591`. */
export interface SettlementBlockEntry {
  entitlementType: string
  entitlementYear: number
  sequence: number
  settlementState: string
  version: number
}
export interface EmploymentEndDateSettlementConflict {
  error: string
  conflictingSettlement: {
    entitlementType: string
    entitlementYear: number
    settlementState: string
  }
  blockingSettlements: SettlementBlockEntry[]
  affectedEntitlementYears: number[]
  reversalEndpoint: string
  hint: string
}
export function isSettlementConflict(body: unknown): body is EmploymentEndDateSettlementConflict {
  if (!body || typeof body !== 'object') return false
  const b = body as Record<string, unknown>
  return Array.isArray(b.blockingSettlements) && typeof b.reversalEndpoint === 'string'
}

/** 409 — existing time entries / absences / work-time registrations fall outside the proposed
    employment window (ADR-040 D3 strand guard). `EmploymentDateEndpoints.cs:798-827`. */
export interface StrandedMonthEntry {
  month: string
  timeEntryCount: number
  absenceCount: number
  workTimeCount: number
}
export interface EmploymentEndDateStrandConflict {
  error: string
  proposedEmploymentStartDate: string | null
  proposedEmploymentEndDate: string | null
  strandedMonths: StrandedMonthEntry[]
  hint: string
}
export function isStrandConflict(body: unknown): body is EmploymentEndDateStrandConflict {
  if (!body || typeof body !== 'object') return false
  const b = body as Record<string, unknown>
  return Array.isArray(b.strandedMonths)
}

/** 422 — the proposed end date lies before the recorded (locked) employment start date
    (`EmploymentDateEndpoints.cs:520-529`). */
export interface InvertedWindowRefusal {
  error: string
  providedEmploymentEndDate: string | null
  recordedEmploymentStartDate: string | null
}
export function isInvertedWindowRefusal(body: unknown): body is InvertedWindowRefusal {
  if (!body || typeof body !== 'object') return false
  const b = body as Record<string, unknown>
  return 'providedEmploymentEndDate' in b && 'recordedEmploymentStartDate' in b
}

/** 412 — stale If-Match (`EmploymentDateEndpoints.cs:502-511` / `:625-634`). */
export interface ConcurrencyRefusal {
  error: string
  expectedVersion: number
  actualVersion: number
}
export function isConcurrencyRefusal(body: unknown): body is ConcurrencyRefusal {
  if (!body || typeof body !== 'object') return false
  const b = body as Record<string, unknown>
  return typeof b.expectedVersion === 'number' && typeof b.actualVersion === 'number'
}

/** 403 — either the self-target exclusion or an OrgScopeValidator denial
    (`EmploymentDateEndpoints.cs:452-457` / `:464-466`). Both wire the same two fields; the
    `reason` text is what tells them apart for a human reader. */
export interface AccessDeniedRefusal {
  error: string
  reason?: string
}
export function isAccessDeniedRefusal(body: unknown): body is AccessDeniedRefusal {
  if (!body || typeof body !== 'object') return false
  const b = body as Record<string, unknown>
  return b.error === 'Access denied'
}

// ════════════════════════════════════════════════════════════════════════════
// The outcome taxonomy for a SUCCESSFUL (200) write.
//
// The response body alone (`IsActive` before vs. after) is enough to tell the four R1(a)-(d)
// branches apart (`EmploymentEndDateLifecycleWriter.cs:228-275`) WITHOUT the frontend
// reimplementing the Copenhagen-business-date "has it passed" comparison — that comparison
// already happened server-side and its result is exactly the `isActive` flip (or lack of one).
// ════════════════════════════════════════════════════════════════════════════

export type TerminationOutcomeKind =
  | 'terminated-now'
  | 'scheduled'
  | 'reactivated'
  | 'cleared-still-inactive'
  | 'corrected-still-terminated'
  | 'corrected-reactivated'
  | 'recorded-no-status-change'

/**
 * Classifies a completed employment-end-date write against the snapshot read BEFORE it, mirroring
 * `EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle`'s R1(a)-(d) table exactly (pinned 1:1
 * against that table in the test file):
 *   (a) was active, set a date, now inactive        → 'terminated-now'
 *   (b) was active, set a date, still active         → 'scheduled' (the date is in the future)
 *   (c) cleared the date, now active                  → 'reactivated'
 *   (c) cleared the date, still inactive               → 'cleared-still-inactive' (manually inactive
 *       for an unrelated reason — clearing claims no provenance to reactivate on)
 *   correction on an already lifecycle-deactivated row, still inactive → 'corrected-still-terminated'
 *   correction on an already lifecycle-deactivated row, now active     → 'corrected-reactivated'
 *       (the corrected date has not passed yet — the row un-deactivates)
 *   (d) set a date on a MANUALLY inactive row (never lifecycle-deactivated) → 'recorded-no-status-change'
 */
export function classifyTerminationOutcome(
  before: Pick<EmploymentEndDateSnapshot, 'isActive' | 'endDateDeactivated'>,
  after: Pick<EmploymentEndDateResponse, 'isActive'>,
  requestedEndDate: string | null,
): TerminationOutcomeKind {
  if (requestedEndDate === null) {
    return after.isActive ? 'reactivated' : 'cleared-still-inactive'
  }
  if (before.isActive) {
    return after.isActive ? 'scheduled' : 'terminated-now'
  }
  if (before.endDateDeactivated) {
    return after.isActive ? 'corrected-reactivated' : 'corrected-still-terminated'
  }
  return 'recorded-no-status-change'
}

// ════════════════════════════════════════════════════════════════════════════
// The hook.
// ════════════════════════════════════════════════════════════════════════════

export function useTermination() {
  /** REQUIRED read — terminated-inclusive, carries the token this screen's PUT must send. */
  const fetchEmploymentEndDate = useCallback(
    async (employeeId: string): Promise<
      { ok: true; data: EmploymentEndDateSnapshot } | { ok: false; error: string; status: number }
    > => {
      const result = await apiFetchWithEtag('/api/admin/employees/{employeeId}/employment-end-date', {
        method: 'GET',
        params: { path: { employeeId } },
      })
      if (!result.ok) return { ok: false, error: result.error, status: result.status }
      return { ok: true, data: toEndDateSnapshot(result.data.data, result.data.etag) }
    },
    [],
  )

  /** Best-effort context read — ACTIVE-ONLY (404s once the employee is terminated); failure is
      swallowed by the caller, never treated as blocking. */
  const fetchEmploymentStartDate = useCallback(
    async (employeeId: string): Promise<EmploymentStartDateResponse | null> => {
      const result = await apiFetchWithEtag('/api/admin/employees/{employeeId}/employment-start-date', {
        method: 'GET',
        params: { path: { employeeId } },
      })
      return result.ok ? result.data.data : null
    },
    [],
  )

  /** Best-effort context + B0 read — terminated-inclusive but anchored on "a row covers today",
      so it can still 404 in the (product-unreachable-today) no-covering-row state; swallowed. */
  const fetchProfileContext = useCallback(
    async (employeeId: string): Promise<EmployeeProfileResponse | null> => {
      const result = await apiFetchWithEtag('/api/admin/employee-profiles/{employeeId}', {
        method: 'GET',
        params: { path: { employeeId } },
      })
      return result.ok ? result.data.data : null
    },
    [],
  )

  /** Best-effort identity read (display name, org, current agreement code) — ACTIVE-ONLY
      (`GetByIdWithVersionAsync(userId, ct)` filters `is_active = TRUE`); ALSO carries a second B0
      field, `scheduledAgreementCode`. 404s once the employee is terminated; swallowed. */
  const fetchIdentity = useCallback(
    async (employeeId: string): Promise<UserDetailResponse | null> => {
      const result = await apiFetchWithEtag('/api/admin/users/{userId}', {
        method: 'GET',
        params: { path: { userId: employeeId } },
      })
      return result.ok ? result.data.data : null
    },
    [],
  )

  /**
   * THE write. `ifMatch` MUST come from `fetchEmploymentEndDate`'s snapshot — never from the
   * best-effort identity/start-date reads, which use a DIFFERENT (active-only) scope and can be
   * stale or absent for the exact population (already-terminated employees) this endpoint exists
   * to correct.
   *
   * Non-throwing: every outcome (success or any of the eight refusal shapes) comes back through
   * the return value so the page can render each distinctly rather than a generic try/catch.
   */
  const setEmploymentEndDate = useCallback(
    async (
      employeeId: string,
      ifMatch: string,
      employmentEndDate: string | null,
    ): Promise<
      | { ok: true; data: EmploymentEndDateResponse; etag: string | null }
      | { ok: false; status: number; error: string; body: unknown }
    > => {
      const result = await apiFetchWithEtag('/api/admin/employees/{employeeId}/employment-end-date', {
        method: 'PUT',
        params: { path: { employeeId } },
        ifMatch,
        body: { employmentEndDate },
      })
      if (!result.ok) return { ok: false, status: result.status, error: result.error, body: result.body }
      return { ok: true, data: result.data.data, etag: result.data.etag }
    },
    [],
  )

  return {
    fetchEmploymentEndDate,
    fetchEmploymentStartDate,
    fetchProfileContext,
    fetchIdentity,
    setEmploymentEndDate,
  }
}
