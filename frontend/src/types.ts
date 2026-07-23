// S120 / TASK-12001 (Typed API Contract retrofit Pass 7, PAT-012) — the
// employee-facing bucket-C drain. The hand-written wire interfaces that lived
// here (TimeEntry, AbsenceEntry, FlexBalanceInfo, the S119 skema-owned
// Project, SkemaRowPreferenceProject/AbsenceType, SkemaRowPreferences,
// SkemaCatalogs, ConsumptionBasisDay, WorkTimeInterval/Day, DailyNormDay,
// SkemaMonthData) were DELETED; the names below are ALIASES onto the GENERATED
// spec types (the S119 keep-names-alive precedent) so the many skema-family
// consumers keep compiling against the SPEC truth. Lie-audit deltas are noted
// per alias.
import type { components } from './lib/api-types'

type Schemas = components['schemas']

/** GET /api/time-entries/{employeeId} row — the GENERATED SharedKernel model.
    Lie-audit (S120): the deleted hand-written interface OMITTED the served
    `voluntaryUnsocialHours` member, typed `startTime`/`endTime`/`taskId`/
    `activityType` as optional-absent where the wire serves them as `null`, and
    wrongly marked the always-served `registeredAt` optional. */
export type TimeEntry = Schemas['StatsTid.SharedKernel.Models.TimeEntry']

/** GET /api/absences/{employeeId} row — the GENERATED SharedKernel model
    (byte-identical to the deleted hand-written interface — faithful). */
export type AbsenceEntry = Schemas['StatsTid.SharedKernel.Models.AbsenceEntry']

/** GET /api/flex-balance/{employeeId} — the ruled S120 ONE shape (owner ruling
    #1): all 5 members always present; the 3 history members are `null` (never
    absent) on the no-history branch; the vestigial `message` member was
    DROPPED backend-side. The deleted hand-written interface modeled the old
    polymorphic branch as optional-absent members. */
export type FlexBalanceInfo = Schemas['StatsTid.Backend.Api.Contracts.FlexBalanceResponse']

export interface WeeklyCalculationResult {
  employeeId: string
  periodStart: string
  periodEnd: string
  agreementCode: string
  okVersion: string
  ruleResults: unknown[]
  flexBalance: unknown
  success: boolean
}

export const ABSENCE_TYPES = [
  { value: 'VACATION', label: 'Ferie (Vacation)' },
  { value: 'CARE_DAY', label: 'Omsorgsdag (Care Day)' },
  { value: 'CHILD_SICK_1', label: 'Barns 1. sygedag (Child Sick Day)' },
  { value: 'PARENTAL_LEAVE', label: 'Barsel (Parental Leave)' },
  { value: 'SENIOR_DAY', label: 'Seniordag (Senior Day)' },
  { value: 'LEAVE_WITH_PAY', label: 'Tjenestefri m. lon (Leave with Pay)' },
  { value: 'LEAVE_WITHOUT_PAY', label: 'Tjenestefri u. lon (Leave without Pay)' },
] as const

export const AGREEMENT_CODES = ['AC', 'HK', 'PROSA'] as const

// S118 / TASK-11801 — the hand-written `LoginRequest` / `LoginResponse`
// interfaces that lived here were DELETED (PAT-012 interface-deletion audit):
// `LoginResponse` OMITTED the `orgId: string | null` member the backend serves.
// The spec-derived bindings live at the single consuming site
// (`contexts/AuthContext.tsx`, via the generated `api-types.ts` schemas).

export interface AuthUser {
  employeeId: string
  role: string
}

// S116 / TASK-11602 (L2) — the hand-written `ApprovalPeriod` interface that
// lived here was DELETED: it claimed 4 phantom members (employeeApprovedAt/By,
// employeeDeadline, managerDeadline) no approval endpoint serves. The
// spec-derived replacements live at the consuming sites:
// `hooks/useApprovals.ts` re-exports the generated `ApprovalPeriodListItem`
// (pending/by-month element) and `pages/approval/MyPeriods.tsx` consumes the
// generated `EmployeePeriodItem` (the wider GET /api/approval/{employeeId}
// element).

// S120 / TASK-12001 — the S119 skema-owned `Project` interface was DELETED:
// its 3 serving surfaces (`SkemaMonthData.projects`, `SkemaCatalogs.projects`,
// the row-preferences project rows) now type onto the spec
// `ProjectResponse` via the aliases below (the S119 admin surface already
// consumes it as `hooks/useProjects.ts` `ProjectItem`). It was byte-identical
// to `ProjectResponse` (projectId/projectCode/projectName/sortOrder) —
// faithful at deletion.

export interface SkemaRow {
  type: 'project' | 'absence'
  key: string
  label: string
  /** S72 / TASK-7202 — optional informational note rendered after the row label
      (e.g. "hele dage"). Informational text ONLY (SPRINT-72 R1 — no full-day snap). */
  note?: string
  /** S73 / TASK-7302 — the served full-day-only flag (R3/R5). When true, an entry
      in this absence row SNAPS to the day's served consumption basis on commit, and
      the grid renders the "hele dage" note from THIS flag — never a hardcoded type
      list. Set from the served absence-type DTOs (deriveSkemaRowBasis). */
  fullDayOnly?: boolean
}

// ── S120 skema-family spec aliases (the month GET / row-preferences family) ──

/** One VISIBLE project row per the user's row preferences (R4) — the ONE spec
    project record (`ProjectResponse`, shared with the month `projects` and
    `catalogs` surfaces). sortOrder is the server-computed DENSE effective
    position (0..n-1) on the row-preferences surface. */
export type SkemaRowPreferenceProject = Schemas['StatsTid.Backend.Api.Contracts.ProjectResponse']

/** One VISIBLE absence-type row per the user's row preferences (R4).
    Lie-audit (S120): `fullDayOnly` is REQUIRED on the wire (the backend always
    emits it) — the deleted hand-written interface marked it optional. */
export type SkemaRowPreferenceAbsenceType =
  Schemas['StatsTid.Backend.Api.Contracts.SkemaRowPreferenceAbsenceRow']

/** The month GET's `rowPreferences` field — the VISIBLE row sets (catalog ∩
    selections when configured; today's fallback when not). Rendering filter
    ONLY (R3) — all grid arithmetic stays over the full served data. */
export type SkemaRowPreferences =
  Schemas['StatsTid.Backend.Api.Contracts.SkemaRowPreferencesResponse']

/** The month GET's `catalogs` field — the ADDABLE sets, selection-INDEPENDENT
    (R4): removed rows stay re-addable; stale selections never resurrect
    org-hidden types. */
export type SkemaCatalogs = Schemas['StatsTid.Backend.Api.Contracts.SkemaCatalogs']

/** S73 — one per-day ADR-032 consumption-basis entry (R3). `hours === null`
    means no dated profile covers the day → NO snap (fail-closed server-side). */
export type ConsumptionBasisDay = Schemas['StatsTid.Backend.Api.Contracts.SkemaDayHoursRow']

export type WorkTimeInterval = Schemas['StatsTid.Backend.Api.Contracts.SkemaWorkIntervalRow']

export type WorkTimeDay = Schemas['StatsTid.Backend.Api.Contracts.SkemaWorkTimeDayRow']

/** Per-day norm row: 0 on weekends; null for academic ANNUAL_ACTIVITY (render
    blank). Same spec record as the consumption basis rows. */
export type DailyNormDay = Schemas['StatsTid.Backend.Api.Contracts.SkemaDayHoursRow']

/** GET /api/skema/{employeeId}/month — THE skema composite, now the GENERATED
    spec record (the S120 graduation). Lie-audit (S120) vs the deleted
    hand-written interface:
    - `entries[].projectCode` is NULLABLE on the wire (the backend maps it from
      the nullable time-entry `taskId`) — the hand-written type claimed
      non-null (a real lie for non-skema-created entries without a task id);
    - `rowPreferences`/`catalogs`/`boundaryWorkTime`/`consumptionBasis` are
      REQUIRED (always served) — hand-written optional;
    - the served top-level `employeeDeadline`/`managerDeadline` members were
      OMITTED entirely;
    - `approval.status` is the 5-value spec enum, not a bare string;
    - absence-type rows' `fullDayOnly` is REQUIRED — hand-written optional. */
export type SkemaMonthData = Schemas['StatsTid.Backend.Api.Contracts.SkemaMonthResponse']
