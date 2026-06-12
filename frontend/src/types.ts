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

export interface LoginRequest {
  username: string
  password: string
}

export interface LoginResponse {
  token: string
  expiresAt: string
  employeeId: string
  role: string
}

export interface AuthUser {
  employeeId: string
  role: string
}

export interface ApprovalPeriod {
  periodId: string
  employeeId: string
  orgId: string
  periodStart: string
  periodEnd: string
  periodType: string
  status: string
  submittedAt: string | null
  approvedBy: string | null
  approvedAt: string | null
  rejectionReason: string | null
  agreementCode: string
  okVersion: string
  createdAt: string
  employeeApprovedAt: string | null
  employeeApprovedBy: string | null
  employeeDeadline: string | null
  managerDeadline: string | null
}

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
  absenceTypes: { type: string; label: string }[]
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
  absenceTypes: { type: string; label: string }[]
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
}
