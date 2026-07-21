export interface TimeEntry {
  employeeId: string
  date: string
  hours: number
  startTime?: string
  endTime?: string
  taskId?: string
  activityType?: string
  agreementCode: string
  okVersion: string
  registeredAt?: string
}

export interface AbsenceEntry {
  employeeId: string
  date: string
  absenceType: string
  hours: number
  agreementCode: string
  okVersion: string
}

export interface FlexBalanceInfo {
  employeeId: string
  balance: number
  previousBalance?: number
  delta?: number
  reason?: string
}

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

export interface Project {
  projectId: string
  projectCode: string
  projectName: string
  isActive: boolean
  sortOrder: number
}

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

// ── S72 / TASK-7201 month-GET additive shapes (consumed by the redesigned grid/page) ──

/** One VISIBLE project row per the user's row preferences (R4). sortOrder is the
    server-computed DENSE effective position (0..n-1), not the raw stored value. */
export interface SkemaRowPreferenceProject {
  projectId: string
  projectCode: string
  projectName: string
  sortOrder: number
}

/** One VISIBLE absence-type row per the user's row preferences (R4). */
export interface SkemaRowPreferenceAbsenceType {
  type: string
  label: string
  sortOrder: number
  /** S73 / TASK-7301 — the served full-day-only flag (R3). An entry in a row of a
      full-day-only type SNAPS to the day's served consumption basis (R5). Optional:
      pre-S73 fixtures/mocks omit it (treated as false). */
  fullDayOnly?: boolean
}

/** The month GET's `rowPreferences` field — the VISIBLE row sets (catalog ∩ selections
    when configured; today's fallback when not). Rendering filter ONLY (R3) — all grid
    arithmetic stays over the full served data. */
export interface SkemaRowPreferences {
  configured: boolean
  projects: SkemaRowPreferenceProject[]
  absenceTypes: SkemaRowPreferenceAbsenceType[]
}

/** The month GET's `catalogs` field — the ADDABLE sets, selection-INDEPENDENT (R4):
    removed rows stay re-addable; stale selections never resurrect org-hidden types. */
export interface SkemaCatalogs {
  projects: Project[]
  // S73 / TASK-7301 — `fullDayOnly` served on the catalog absence-type DTOs (R3),
  // additive (pre-S73 fixtures omit it → treated as false).
  absenceTypes: { type: string; label: string; fullDayOnly?: boolean }[]
}

/** S73 / TASK-7301 — one per-day ADR-032 consumption-basis entry (R3). The FE
    full-day snap (R5) reads `consumptionBasis[date].hours`; `hours === null`
    means no dated profile covers the day → NO snap (the typed entry stands
    locally; the server rejects via the anchor-422 family — fail-closed). Derived
    from the SAME ConsumptionCalculator path the backend guard demands (the
    served==guard identity, R3). */
export interface ConsumptionBasisDay {
  date: string         // "yyyy-MM-dd"
  hours: number | null // null = no dated profile covers the day → no snap (R5)
}

export interface WorkTimeInterval {
  start: string  // "HH:mm"
  end: string    // "HH:mm"
}

export interface WorkTimeDay {
  date: string  // "YYYY-MM-DD"
  intervals: WorkTimeInterval[]
  manualHours: number
}

export interface DailyNormDay {
  date: string         // "YYYY-MM-DD"
  hours: number | null // 0 on weekends; null for academic ANNUAL_ACTIVITY (render blank)
}

export interface SkemaMonthData {
  year: number
  month: number
  daysInMonth: number
  projects: Project[]
  // S73 / TASK-7301 — `fullDayOnly` served on the month-GET absence-type DTOs (R3),
  // additive (pre-S73 fixtures omit it → treated as false).
  absenceTypes: { type: string; label: string; fullDayOnly?: boolean }[]
  entries: { date: string; projectCode: string; hours: number }[]
  // S72 / TASK-7201 — `feriedage` is the ADR-032 recorded per-absence day-equivalent,
  // served verbatim (nullable passthrough: null on zero-norm days / non-entitlement
  // rows; consumers SKIP null-valued rows when summing — SPRINT-72 R10 / Reviewer N4).
  absences: { date: string; absenceType: string; hours: number; feriedage?: number | null }[]
  approval: {
    periodId: string
    status: string
    employeeDeadline: string | null
    managerDeadline: string | null
    employeeApprovedAt: string | null
    rejectionReason: string | null
  } | null
  workTime: WorkTimeDay[]
  dailyNorm: DailyNormDay[]
  // ── S72 / TASK-7201 additive month-GET fields (optional: pre-S72 fixtures/mocks
  // omit them; the grid falls back to rendering all served rows — SPRINT-72 R12) ──
  /** VISIBLE row sets + order (R4). Rendering filter ONLY (R3). */
  rowPreferences?: SkemaRowPreferences
  /** ADDABLE catalogs, selection-independent (R4) — the manager modal's right pane. */
  catalogs?: SkemaCatalogs
  /** 0..2 boundary-day workTime rows (prev-month last day / next-month first day)
      for the client-side §J 11-hour rest analysis (SPRINT-72 R6). */
  boundaryWorkTime?: WorkTimeDay[]
  /** The employee's weekday full-day norm at the viewed month's LAST day (R10, D-A
      hours-first cards). Null = fail-soft (no dated profile / ANNUAL_ACTIVITY). */
  fullDayNormAtMonthEnd?: number | null
  /** S73 / TASK-7301 — the per-day ADR-032 consumption basis (R3), one entry per
      day of the viewed month. The full-day snap (R5) reads
      `consumptionBasis[date].hours`; null = no dated profile → no snap. Optional:
      pre-S73 fixtures/mocks omit it (no snap data → typed value stands). */
  consumptionBasis?: ConsumptionBasisDay[]
}
