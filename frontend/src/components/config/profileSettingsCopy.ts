// The institution's own flex, office-hours and paid-absence settings on the local profile (ADR-043 D6):
// every Danish string of the editor card and of the history row lives here once, with the field
// metadata both screens read. The settings are stored and shown; no calculation reads them yet.
import type { LocalAgreementProfile } from '../../hooks/useConfig'

export const CARD_HEADING = 'Institutionens egne regler for flex, kontortid og betalt fravær'

// One note, identical on every setting, true while no calculation or pay reads them.
export const D6_NOTE =
  'Gemmes nu, men bruges endnu ikke i nogen beregning og påvirker endnu ikke lønberegningen.'

// Shown on the AC family only: no central text sources a paid sick day's credit for AC staff
// (ADR-043 D2, provisional item 3); the HK/PROSA rule is the labelled default by analogy.
export const D8B_LABEL =
  'Standard ved analogi: Ingen central aftaletekst fastsætter, hvordan en betalt sygedag tæller for AC-ansatte. StatsTid bruger derfor reglen for HK og PROSA (§ 7 nr. 2) som standard.'

export const KONTORTID_HINT = 'Kræver kontortid for mindst én ugedag.'

export const HISTORY_NULL = '(ikke angivet)'

export function officeHoursWarning(sumMinutes: number, normHours: number): string {
  return `Kontortiden er i alt ${formatHours(sumMinutes / 60)} timer om ugen, men den ugentlige norm er ${formatHours(normHours)} timer. Du kan stadig gemme.`
}

/** Decimal comma, at most two decimals, no trailing zeros: 36 -> "36", 36.5 -> "36,5". */
export function formatHours(n: number): string {
  return String(Math.round(n * 100) / 100).replace('.', ',')
}

/** Shown in the banner for a 400 entry naming a field the editor does not show, and under a known
 * control for a code the editor has no text for (never the raw code). */
export const UNPLACEABLE_REFUSAL = 'Værdien kunne ikke gemmes.'

export const CODE_MESSAGES: Record<string, string> = {
  MUST_BE_POSITIVE: 'Skal være større end 0.',
  MUST_NOT_BE_NEGATIVE: 'Må ikke være negativ.',
  TOO_MANY_DECIMALS: 'Brug højst to decimaler.',
  EXCEEDS_STORAGE_LIMIT: 'Tallet er for stort til at blive gemt.',
  UNKNOWN_VALUE: 'Ugyldig værdi. Vælg en af de viste muligheder.',
  NOT_WHOLE_MINUTE: 'Angiv klokkeslættet i hele minutter.',
  WINDOW_INCOMPLETE: 'Angiv både fra og til, eller lad begge stå tomme.',
  WINDOW_START_NOT_BEFORE_END: '"Til" skal være senere end "fra".',
  OFFICE_HOURS_WITHOUT_WINDOWS:
    'Kontortid kan kun vælges som grundlag, når der er angivet kontortid for mindst én ugedag.',
}

// ── The nine fields, in screen order. ──

export type ChoiceMember =
  | 'flexSettlementPeriod'
  | 'flexCapTestPeriod'
  | 'flexAboveCapAction'
  | 'flexLeavingAction'
  | 'paidAbsenceCreditBasis'
export type CapMember = 'flexPlusCapHours' | 'flexMinusCapHours' | 'flexDailyCreditCapHours'

export const WEEKDAYS = [
  { key: 'Mon', name: 'Mandag' },
  { key: 'Tue', name: 'Tirsdag' },
  { key: 'Wed', name: 'Onsdag' },
  { key: 'Thu', name: 'Torsdag' },
  { key: 'Fri', name: 'Fredag' },
] as const

type WeekdayKey = (typeof WEEKDAYS)[number]['key']
export type OfficeHoursMember = `officeHours${WeekdayKey}${'Start' | 'End'}`
export type SettingMember = ChoiceMember | CapMember | OfficeHoursMember

export const OFFICE_HOURS_MEMBERS: OfficeHoursMember[] = WEEKDAYS.flatMap(
  d => [`officeHours${d.key}Start`, `officeHours${d.key}End`] as OfficeHoursMember[],
)

export const OFFICE_HOURS_LEGEND = 'Kontortid'

export interface ChoiceSpec {
  member: ChoiceMember
  legend: string
  notSet: string
  options: { value: string; label: string }[]
}

const PARAGRAPH_7_LABEL =
  '§ 7 nr. 2: de timer, medarbejderen skulle have arbejdet den dag; ellers 7,4 timer, forholdsmæssigt ved deltid eller plustid'

export const CHOICE_SPECS: Record<ChoiceMember, ChoiceSpec> = {
  flexSettlementPeriod: {
    member: 'flexSettlementPeriod',
    legend: 'Afregningsperiode for flex',
    notSet: 'Ikke angivet (kalendermåneden gælder)',
    options: [{ value: 'MONTH', label: 'Kalendermåned' }],
  },
  flexCapTestPeriod: {
    member: 'flexCapTestPeriod',
    legend: 'Periode, som flexloftet måles over',
    notSet: 'Ikke angivet (samme som afregningsperioden)',
    options: [{ value: 'MONTH', label: 'Kalendermåned' }],
  },
  flexAboveCapAction: {
    member: 'flexAboveCapAction',
    legend: 'Timer over loftet for plussaldo',
    notSet: 'Ikke angivet (timerne bortfalder)',
    options: [{ value: 'FORFEIT', label: 'Bortfalder' }],
  },
  flexLeavingAction: {
    member: 'flexLeavingAction',
    legend: 'Flexsaldo ved fratræden',
    notSet:
      'Ikke angivet (foreløbig standard: en positiv saldo bortfalder, en negativ registreres og modregnes ikke)',
    options: [
      {
        value: 'FORFEIT_POSITIVE_RECORD_NEGATIVE',
        label: 'En positiv saldo bortfalder, en negativ registreres og modregnes ikke',
      },
    ],
  },
  paidAbsenceCreditBasis: {
    member: 'paidAbsenceCreditBasis',
    legend: 'Grundlag for godskrivning af betalt fravær',
    notSet: 'Ikke angivet (§ 7 nr. 2 gælder)',
    options: [
      { value: 'PARAGRAPH_7_NR_2', label: PARAGRAPH_7_LABEL },
      { value: 'OFFICE_HOURS', label: 'Kontortid: ugedagens kontortid' },
    ],
  },
}

export const CAP_LABELS: Record<CapMember, string> = {
  flexPlusCapHours: 'Loft for plussaldo på flex (timer)',
  flexMinusCapHours: 'Loft for minussaldo på flex (timer, uden minustegn)',
  flexDailyCreditCapHours: 'Loft for daglig optjening af flex (timer)',
}

/** Screen order of fields 1-7 and 9; field 8 (office hours) sits between them. */
export const FIELDS_BEFORE_OFFICE_HOURS: (ChoiceMember | CapMember)[] = [
  'flexSettlementPeriod',
  'flexCapTestPeriod',
  'flexPlusCapHours',
  'flexMinusCapHours',
  'flexDailyCreditCapHours',
  'flexAboveCapAction',
  'flexLeavingAction',
]

export function isChoiceMember(m: SettingMember): m is ChoiceMember {
  return m in CHOICE_SPECS
}

// ── The history row. ──

/** The nine new lines under a closed profile's four legacy values, in screen order. */
export function historySettingLines(row: LocalAgreementProfile): { label: string; value: string }[] {
  const line = (m: ChoiceMember | CapMember) => {
    if (isChoiceMember(m)) {
      const spec = CHOICE_SPECS[m]
      const v = row[m]
      return { label: spec.legend, value: v ? (spec.options.find(o => o.value === v)?.label ?? v) : HISTORY_NULL }
    }
    const v = row[m]
    return { label: CAP_LABELS[m], value: v === null || v === undefined ? HISTORY_NULL : `${formatHours(v)} timer` }
  }
  const days = WEEKDAYS.flatMap(d => {
    const from = row[`officeHours${d.key}Start`]
    const to = row[`officeHours${d.key}End`]
    return from && to ? [`${d.name.toLowerCase()} ${from.slice(0, 5)}–${to.slice(0, 5)}`] : []
  })
  return [
    ...FIELDS_BEFORE_OFFICE_HOURS.map(line),
    { label: OFFICE_HOURS_LEGEND, value: days.length > 0 ? days.join(', ') : HISTORY_NULL },
    line('paidAbsenceCreditBasis'),
  ]
}
