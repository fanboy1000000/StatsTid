/**
 * Shared allocation/work-time helpers for the Skema grid + summary card.
 *
 * The "Ikke fordelt" (unallocated) balance compares worked hours against
 * project-allocated hours per day. The BALANCED (green) set MUST match the set
 * the backend allocation gate accepts: round both sides to 2 decimals, then
 * treat |worked - allocated| < ALLOCATION_TOLERANCE as balanced.
 */
export const ALLOCATION_TOLERANCE = 0.005

export type AllocationState = 'balanced' | 'under' | 'over'

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/**
 * Classify a day's allocation balance.
 * - balanced: |worked - allocated| < tolerance (after 2dp rounding)
 * - under: worked > allocated (hours still to distribute onto projects)
 * - over: allocated > worked (more allocated than registered work time)
 */
export function classifyAllocation(worked: number, allocated: number): AllocationState {
  const w = round2(worked)
  const a = round2(allocated)
  if (Math.abs(w - a) < ALLOCATION_TOLERANCE) return 'balanced'
  return w > a ? 'under' : 'over'
}

/** Unallocated hours for a day/month: worked - allocated, rounded to 2dp. */
export function unallocated(worked: number, allocated: number): number {
  return round2(round2(worked) - round2(allocated))
}
