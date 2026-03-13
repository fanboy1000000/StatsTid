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

export interface TimerSessionEntry {
  sessionId: string
  checkInAt: string
  checkOutAt: string | null
  isActive: boolean
}

export interface TimerSession {
  sessionId: string
  employeeId: string
  date: string
  checkInAt: string
  checkOutAt: string | null
  isActive: boolean
  sessions?: TimerSessionEntry[]
}

export interface SkemaRow {
  type: 'project' | 'absence'
  key: string
  label: string
}

export interface SkemaMonthData {
  year: number
  month: number
  daysInMonth: number
  projects: Project[]
  absenceTypes: { type: string; label: string }[]
  entries: { date: string; projectCode: string; hours: number }[]
  absences: { date: string; absenceType: string; hours: number }[]
  timerSession: TimerSession | null
  approval: {
    periodId: string
    status: string
    employeeDeadline: string | null
    managerDeadline: string | null
    employeeApprovedAt: string | null
    rejectionReason: string | null
  } | null
  arrivalDepartures: { date: string; arrival: string | null; departure: string | null }[]
}
