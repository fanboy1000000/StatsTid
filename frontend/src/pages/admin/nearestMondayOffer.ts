import { formatEffectiveDateLong } from './editPerson/EffectiveDatePicker'

// The server is the authority on an "applies from" date: it refuses a future date, a date before the
// version it replaces, and a mid-week date when a week-read field changes (QUAL-182, ADR-043 D12).
// This helper reads such a refusal and returns only what the server would accept now, so the screen
// never offers a date it will refuse again (NR-P14, NR-P17). Only these three codes get the Danish
// handling; every other error keeps the page's existing message.

/** The 400 body's `fields` member, as the two editors' mutation errors carry it. */
export interface DateRefusalError {
  status: number
  body?: { fields?: { field?: string; code?: string; nearestValid?: string[] }[] }
}

export interface DateRefusal {
  /** The sentence shown to the administrator (Danish). */
  text: string
  /** Buttons that each set the date field to `date`, in date order. */
  buttons: { label: string; date: string }[]
}

const MONDAY_LEAD = 'Ændringen påvirker ugeberegningen og skal derfor gælde fra en mandag.'

/** The first Monday after `today` (ISO yyyy-MM-dd), by UTC calendar arithmetic. */
export function nextMonday(today: string): string {
  const d = new Date(`${today}T00:00:00Z`)
  const dow = d.getUTCDay() // 0 = Sunday, 1 = Monday
  d.setUTCDate(d.getUTCDate() + (dow === 1 ? 7 : (8 - dow) % 7))
  return d.toISOString().slice(0, 10)
}

function mondayText(date: string): string {
  return `mandag den ${formatEffectiveDateLong(date)}`
}

/**
 * @param today the Copenhagen business day (`useCalendarToday()`)
 * @param newestVersion the date of the newest version of the key (the lower limit), or null when none
 * @returns null when the error is not one of the three date refusals
 */
export function dateRefusalFrom(
  err: DateRefusalError,
  today: string,
  newestVersion: string | null,
): DateRefusal | null {
  if (err.status !== 400) return null
  const fields = err.body?.fields
  if (!Array.isArray(fields)) return null
  const byCode = (code: string) => fields.find((f) => f.code === code)

  if (byCode('EFFECTIVE_FROM_NOT_TODAY_OR_PAST')) {
    return {
      text: 'Datoen kan ikke ligge efter i dag.',
      buttons: [{ label: 'Brug dags dato', date: today }],
    }
  }

  const predecessor = byCode('BEFORE_PREDECESSOR')?.nearestValid?.[0]
  if (predecessor) {
    return {
      text: `Datoen kan tidligst være ${formatEffectiveDateLong(predecessor)}, hvor den seneste version gælder fra.`,
      buttons: [{ label: `Brug ${formatEffectiveDateLong(predecessor)}`, date: predecessor }],
    }
  }

  const monday = byCode('NOT_MONDAY_ALIGNED')
  if (monday) {
    // The server lists the previous and the next Monday without filtering by today or by the
    // predecessor; keep only those it would accept now.
    const offered = (monday.nearestValid ?? [])
      .filter((d) => d <= today && (newestVersion === null || d >= newestVersion))
      .sort()
    if (offered.length === 0) {
      return {
        text: `${MONDAY_LEAD} Ingen mandag kan bruges i dag: den tidligst mulige dato er ${mondayText(nextMonday(today))}, og ændringen kan først gemmes den dag.`,
        buttons: [],
      }
    }
    const buttons = offered.map((date) => ({ label: `Brug ${formatEffectiveDateLong(date)}`, date }))
    const choice = offered.map(mondayText).join(' eller ')
    return { text: `${MONDAY_LEAD} Du kan bruge ${choice}.`, buttons }
  }

  return null
}
