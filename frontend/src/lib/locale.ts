export const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

export function formatMonthLabel(year: number, month: number): string {
  return `${DANISH_MONTHS[month - 1]} ${year}`
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
