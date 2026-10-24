export const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

export function formatMonthLabel(year: number, month: number): string {
  return `${DANISH_MONTHS[month - 1]} ${year}`
}

/** S146 / TASK-14608 — shared empty-state text for a flex balance with no settled (paid) month. */
export const NO_SETTLED_MONTH = 'ingen afregnet måned'

/**
 * Server month "2026-09" -> "september 2026" (lower-case, as in running Danish text).
 * Returns null for null or anything that is not a well-formed `YYYY-MM` with month 1–12;
 * never throws, so callers fail closed (empty state / dash) on a contract slip.
 */
export function formatYearMonthWord(ym: string | null | undefined): string | null {
  if (typeof ym !== 'string') return null
  const m = /^(\d{4})-(\d{2})$/.exec(ym)
  if (!m) return null
  const month = Number(m[2])
  if (month < 1 || month > 12) return null
  return `${DANISH_MONTHS[month - 1].toLowerCase()} ${m[1]}`
}

/**
 * Parse a numeric string that may use a Danish decimal comma (`7,4`).
 * Returns null for empty input, NaN-producing input handled by caller.
 */
export function parseDanishNumber(value: string): number {
  return parseFloat(value.replace(',', '.'))
}

/** Format a number for display using a Danish decimal comma, trimming a trailing `,0`. */
export function formatDanishNumber(value: number, decimals = 1): string {
  const s = value.toFixed(decimals).replace('.', ',')
  // Trim trailing comma-zero(s) for clean integers (e.g. "7,0" -> "7")
  return s.replace(/,0+$/, '')
}
