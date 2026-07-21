// Shared entitlement constants used by both the standalone
// EntitlementConfigEditor page and the inline EntitlementSection component.

export type EntitlementType =
  | 'VACATION'
  | 'SPECIAL_HOLIDAY'
  | 'CARE_DAY'
  | 'CHILD_SICK'
  | 'SENIOR_DAY'

export type AccrualModel = 'IMMEDIATE' | 'MONTHLY_ACCRUAL'

export const TYPE_LABELS: Record<EntitlementType, string> = {
  VACATION: 'Ferie',
  SPECIAL_HOLIDAY: 'Særlig feriedag',
  CARE_DAY: 'Omsorgsdag',
  CHILD_SICK: 'Barnets sygedag',
  SENIOR_DAY: 'Seniordag',
}

export const TYPE_OPTIONS: EntitlementType[] = [
  'VACATION',
  'SPECIAL_HOLIDAY',
  'CARE_DAY',
  'CHILD_SICK',
  'SENIOR_DAY',
]

export const ACCRUAL_OPTIONS: AccrualModel[] = ['IMMEDIATE', 'MONTHLY_ACCRUAL']

export const ACCRUAL_LABELS: Record<AccrualModel, string> = {
  IMMEDIATE: 'Straks',
  MONTHLY_ACCRUAL: 'Maanedlig optjening',
}

// S118 / TASK-11801 — the spec-derived row types carry `entitlementType` /
// `accrualModel` as OPEN `string` on the wire (deliberately NOT spec-enums —
// the S113 rule: open config-keyed sets must not be declared closed). These
// runtime guards narrow a wire string to the UI's known set without an `as`
// cast (the PAT-012 no-`as` convention), and the label helpers fall back to
// the raw value for an unknown member (previously rendered `undefined`).
export function isEntitlementType(value: string): value is EntitlementType {
  return TYPE_OPTIONS.some((t) => t === value)
}

export function isAccrualModel(value: string): value is AccrualModel {
  return ACCRUAL_OPTIONS.some((m) => m === value)
}

export function entitlementTypeLabel(value: string): string {
  return isEntitlementType(value) ? TYPE_LABELS[value] : value
}

export function accrualModelLabel(value: string): string {
  return isAccrualModel(value) ? ACCRUAL_LABELS[value] : value
}

export const MONTH_LABELS: Record<number, string> = {
  1: 'Januar',
  2: 'Februar',
  3: 'Marts',
  4: 'April',
  5: 'Maj',
  6: 'Juni',
  7: 'Juli',
  8: 'August',
  9: 'September',
  10: 'Oktober',
  11: 'November',
  12: 'December',
}
