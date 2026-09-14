// SPRINT-141 / TASK-14108 — pure, unit-testable presentation logic for the termination screen.
// Kept separate from the page component so the "what does each outcome mean for HR" mapping can
// be pinned by tests without rendering React. Every string is Danish (the app's UI language) and
// every refusal is worded as an instruction for what HR does next, per this task's brief — never
// as a restatement of the HTTP shape the server returned.
import {
  isAccessDeniedRefusal,
  isConcurrencyRefusal,
  isInvertedWindowRefusal,
  isSettlementConflict,
  isStrandConflict,
  type EmploymentEndDateStrandConflict,
  type EmploymentEndDateSettlementConflict,
  type TerminationOutcomeKind,
} from '../../../hooks/useTermination'

/** DateOnly ("YYYY-MM-DD") formatter that never shifts a day via timezone conversion — parses the
    Y/M/D parts directly rather than handing the string to `new Date(iso)` (which parses as UTC
    midnight and can render as the PRECEDING day in a timezone behind UTC). */
export function formatDateOnly(iso: string | null | undefined): string {
  if (!iso) return '–'
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!match) return iso
  const [, y, m, d] = match
  return new Date(Number(y), Number(m) - 1, Number(d)).toLocaleDateString('da-DK')
}

export interface SuccessCopy {
  title: string
  description: string
}

/** The 200-OK outcome taxonomy, worded for HR (see `useTermination.classifyTerminationOutcome`
    for how the kind is derived from the before/after snapshot). */
export function successCopy(kind: TerminationOutcomeKind, date: string | null): SuccessCopy {
  const d = formatDateOnly(date)
  switch (kind) {
    case 'terminated-now':
      return {
        title: 'Fratrædelse registreret',
        description: `Ansættelsen er afsluttet med virkning fra ${d}. Medarbejderen er nu inaktiv.`,
      }
    case 'scheduled':
      return {
        title: 'Fratrædelse planlagt',
        description: `Fratrædelsen er registreret til ${d}. Medarbejderen forbliver aktiv indtil da og deaktiveres automatisk på dagen — der kræves ingen yderligere handling.`,
      }
    case 'reactivated':
      return {
        title: 'Fratrædelse fortrudt',
        description: 'Fratrædelsesdatoen er fjernet, og medarbejderen er aktiv igen.',
      }
    case 'cleared-still-inactive':
      return {
        title: 'Dato fjernet',
        description:
          'Fratrædelsesdatoen er fjernet. Medarbejderen er fortsat inaktiv af en anden årsag og er ikke blevet genaktiveret af denne handling.',
      }
    case 'corrected-still-terminated':
      return {
        title: 'Dato rettet',
        description: `Fratrædelsesdatoen er rettet til ${d}. Medarbejderen er fortsat registreret som fratrådt.`,
      }
    case 'corrected-reactivated':
      return {
        title: 'Dato rettet',
        description: `Fratrædelsesdatoen er rettet til ${d}, som endnu ikke er passeret — medarbejderen er derfor aktiv igen indtil da.`,
      }
    case 'recorded-no-status-change':
      return {
        title: 'Dato registreret',
        description: `Fratrædelsesdatoen ${d} er registreret. Medarbejderens status er uændret (inaktiv af en anden årsag end fratrædelse).`,
      }
  }
}

/** The refusal taxonomy this screen distinguishes. Every `message` tells HR what to do next. */
export type RefusalState =
  | { kind: 'self'; message: string }
  | { kind: 'access-denied'; message: string }
  | { kind: 'not-found'; message: string }
  | { kind: 'concurrency'; message: string; expectedVersion: number; actualVersion: number }
  | { kind: 'inverted-window'; message: string; provided: string | null; recordedStart: string | null }
  | { kind: 'settlement-conflict'; message: string; body: EmploymentEndDateSettlementConflict }
  | { kind: 'strand-conflict'; message: string; body: EmploymentEndDateStrandConflict }
  | { kind: 'missing-precondition'; message: string }
  | { kind: 'unknown'; message: string; status: number }

/** Formats one blocking settlement row for the settlement-conflict refusal. */
function settlementRowLabel(row: EmploymentEndDateSettlementConflict['blockingSettlements'][number]): string {
  return `${row.entitlementType} ${row.entitlementYear} (${row.settlementState.toLowerCase()})`
}

/** Formats one stranded month row for the strand-conflict refusal. */
function strandedMonthLabel(row: EmploymentEndDateStrandConflict['strandedMonths'][number]): string {
  const parts: string[] = []
  if (row.timeEntryCount > 0) parts.push(`${row.timeEntryCount} tidsregistrering(er)`)
  if (row.absenceCount > 0) parts.push(`${row.absenceCount} fraværsregistrering(er)`)
  if (row.workTimeCount > 0) parts.push(`${row.workTimeCount} arbejdstidsregistrering(er)`)
  return `${row.month}: ${parts.join(', ') || 'registreringer'}`
}

/**
 * Classifies a failed PUT into the taxonomy this screen distinguishes, from the HTTP status and
 * (for the two hand-written 409 shapes, plus the 422/412/403 shapes) the parsed body. Every
 * message is written as an instruction — what HR does next — never as a restatement of the
 * server's HTTP shape.
 */
export function classifyRefusal(status: number, body: unknown, error: string): RefusalState {
  if (status === 404) {
    return {
      kind: 'not-found',
      message:
        'Medarbejderen kunne ikke findes. Siden er muligvis forældet, eller medarbejder-id er forkert — prøv at genindlæse siden.',
    }
  }
  if (status === 428) {
    return {
      kind: 'missing-precondition',
      message:
        'Intern fejl: anmodningen manglede en versionsangivelse. Dette er en fejl i klienten, ikke i sagen — genindlæs siden og prøv igen, eller kontakt udvikling hvis det gentager sig.',
    }
  }
  if (status === 412 && isConcurrencyRefusal(body)) {
    return {
      kind: 'concurrency',
      message:
        'En anden har ændret denne medarbejders oplysninger, siden siden blev indlæst. Siden genindlæses — gennemgå de aktuelle oplysninger og prøv igen.',
      expectedVersion: body.expectedVersion,
      actualVersion: body.actualVersion,
    }
  }
  if (status === 422 && isInvertedWindowRefusal(body)) {
    return {
      kind: 'inverted-window',
      message: `Den valgte fratrædelsesdato (${formatDateOnly(body.providedEmploymentEndDate)}) ligger før den registrerede ansættelsesstart (${formatDateOnly(body.recordedEmploymentStartDate)}). Vælg en dato på eller efter ansættelsesstarten.`,
      provided: body.providedEmploymentEndDate,
      recordedStart: body.recordedEmploymentStartDate,
    }
  }
  if (status === 409 && isSettlementConflict(body)) {
    const rows = body.blockingSettlements.map(settlementRowLabel).join('; ')
    return {
      kind: 'settlement-conflict',
      message: `Dette kan ikke gennemføres, fordi der allerede findes en afregnet feriesaldo for: ${rows}. Reverér afregningen først (${body.reversalEndpoint}), og prøv derefter igen.`,
      body,
    }
  }
  if (status === 409 && isStrandConflict(body)) {
    const rows = body.strandedMonths.map(strandedMonthLabel).join('; ')
    return {
      kind: 'strand-conflict',
      message: `Dette kan ikke gennemføres, fordi der findes registreringer uden for den foreslåede ansættelsesperiode i: ${rows}. Ret eller fjern registreringerne i disse måneder først, eller vælg en dato der dækker dem.`,
      body,
    }
  }
  if (status === 403 && isAccessDeniedRefusal(body)) {
    const reason = body.reason ?? ''
    if (reason.startsWith('Own employment end date')) {
      return {
        kind: 'self',
        message: 'Du kan ikke afslutte din egen ansættelse. Bed en anden HR-administrator om at gøre dette.',
      }
    }
    return {
      kind: 'access-denied',
      message: reason
        ? `Adgang nægtet: ${reason}`
        : 'Adgang nægtet. Du har ikke rettigheder til at ændre denne medarbejders fratrædelsesdato.',
    }
  }
  return { kind: 'unknown', message: `Kunne ikke gennemføre ændringen (fejl ${status}): ${error}`, status }
}
