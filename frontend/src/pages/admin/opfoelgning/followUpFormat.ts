// SPRINT-140 / TASK-14006 — small formatting helpers shared by the HR
// follow-up landing page and its process lists. Kept local to this page
// (not `lib/`) — nothing outside `admin/opfoelgning` consumes them.

/** Whole days from `fromIso` to `toIso` (both plain `YYYY-MM-DD` dates, the
    `date` format every follow-up response uses for `today` / the anchor
    fields). Parsed as UTC midnight so a date-only diff is never off by one
    from a viewer's local timezone. */
export function daysBetween(fromIso: string, toIso: string): number {
  const from = Date.parse(`${fromIso}T00:00:00Z`)
  const to = Date.parse(`${toIso}T00:00:00Z`)
  return Math.round((to - from) / 86_400_000)
}

/** "0 dage" / "1 dag" / "N dage" — Danish singular/plural. */
export function daysLabel(days: number): string {
  return days === 1 ? '1 dag' : `${days} dage`
}

/** A plain Danish date (`da-DK`) for a `date` or `date-time` string — used
    where no day-count is available at all (payout-pending carries no
    `today` reference to diff against). Falls back to the raw string if the
    value cannot be parsed, matching the codebase's other `formatDate`
    helpers (e.g. `WorklistList.tsx`). */
export function formatDaDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('da-DK')
  } catch {
    return iso
  }
}

/** "1. november 2025" — the §21 tile's window-closed state ("Åbner …") reads
    better as a spelled-out date than the raw `YYYY-MM-DD` the response
    carries in `windowOpensOn`. Formatted in UTC explicitly — a plain `date`
    string parsed as UTC midnight and then rendered in a viewer's LOCAL
    timezone can roll back a day (e.g. any timezone behind UTC), which would
    make this text non-deterministic across machines. */
export function formatDaLongDate(iso: string): string {
  try {
    return new Date(`${iso}T00:00:00Z`).toLocaleDateString('da-DK', {
      day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC',
    })
  } catch {
    return iso
  }
}
