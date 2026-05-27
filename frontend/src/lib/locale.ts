export const DANISH_MONTHS = [
  'Januar', 'Februar', 'Marts', 'April', 'Maj', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'December',
]

export function formatMonthLabel(year: number, month: number): string {
  return `${DANISH_MONTHS[month - 1]} ${year}`
}
