// The institution's customary days (kutymefridage): a list, a form to record one, and the two
// dated actions on a row, "Afslut" (withdraw from a date) and "Genindfør" (reinstate from a date).
// Calls GET and POST /api/config/{orgId}/customary-days and POST .../{customId}/withdraw and
// .../reinstate through the typed client. The server is the authority on every rule; this editor only
// declines to offer what it knows the server refuses, and shows each refusal that still arrives
// (a race, an old tab) in Danish on its field. Customs reach no pay calculation yet, and the card
// says so with the one sentence the profile editor uses (profileSettingsCopy.D6_NOTE).
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { apiClient, apiFetchWithEtag } from '../../lib/api'
import type { components } from '../../lib/api-types'
import { Alert, Button, Card, FormField, Input, Radio, Select, Spinner, Table } from '../ui'
import { CODE_MESSAGES, D6_NOTE, UNPLACEABLE_REFUSAL, formatHours } from './profileSettingsCopy'

type CustomaryDay = components['schemas']['StatsTid.Backend.Api.Contracts.CustomaryDayResponse']
type CreateBody = components['schemas']['StatsTid.Backend.Api.Endpoints.CustomaryDayEndpoints.CreateCustomaryDayRequest']

interface Option { value: string; label: string }

interface CustomaryDaysEditorProps {
  orgId: string
  agreementOptions: Option[]
}

// ── Copy (ruled Danish) ──

const MONTHS = [
  'januar', 'februar', 'marts', 'april', 'maj', 'juni',
  'juli', 'august', 'september', 'oktober', 'november', 'december',
]
const MONTH_OPTIONS: Option[] = MONTHS.map((m, i) => ({ value: String(i + 1), label: m }))
const ALL_AGREEMENTS = 'ALL'
const HEADERS = ['Dag', 'Type', 'Omfang', 'Tildeling', 'Overenskomst', 'Gælder fra', 'Ophører fra', 'Handlinger']

const KIND_OPTIONS: Option[] = [
  { value: 'PAID_DAY_OFF', label: 'Betalt fridag' },
  { value: 'CLOSURE_DAY', label: 'Lukkedag' },
]
const PART_OPTIONS: Option[] = [
  { value: 'WHOLE_DAY', label: 'Hele dagen' },
  { value: 'FROM_TIME', label: 'Fra et klokkeslæt' },
  { value: 'HOURS', label: 'Et antal timer' },
]
const MODE_OPTIONS: Option[] = [
  { value: 'GIVEN_TO_ALL', label: 'Gives til alle' },
  { value: 'ON_REQUEST', label: 'Efter anmodning' },
]
const DAY_FORM_OPTIONS: Option[] = [
  { value: 'yearly', label: 'Hvert år på samme dato' },
  { value: 'single', label: 'En enkelt dato' },
]

const CODE_TEXT: Record<string, string> = {
  DAY_FORM_INVALID: 'Vælg enten en dato hvert år eller en enkelt dato.',
  DAY_INCOMPLETE: 'Angiv både måned og dag.',
  NOT_A_CALENDAR_DAY: 'Datoen findes ikke.',
  REQUIRED: 'Skal udfyldes.',
  NOT_ALLOWED: 'Må ikke udfyldes ved dette valg.',
  UNKNOWN_VALUE: CODE_MESSAGES.UNKNOWN_VALUE,
  NOT_WHOLE_MINUTE: CODE_MESSAGES.NOT_WHOLE_MINUTE,
  MUST_BE_POSITIVE: CODE_MESSAGES.MUST_BE_POSITIVE,
  TOO_MANY_DECIMALS: CODE_MESSAGES.TOO_MANY_DECIMALS,
  EXCEEDS_STORAGE_LIMIT: CODE_MESSAGES.EXCEEDS_STORAGE_LIMIT,
  VALID_TO_NOT_AFTER_VALID_FROM: 'Datoen skal ligge efter den dato, dagen gælder fra.',
}

const OVERLAP_TEXT = 'Institutionen har allerede denne dag registreret for samme overenskomst i den valgte periode.'
const ALREADY_STOPPED_TEXT = 'Dagen er allerede afsluttet. Genindlæs siden.'
const NOT_STOPPED_TEXT = 'Dagen er ikke afsluttet. Genindlæs siden.'
const FORBIDDEN_TEXT = 'Du har ikke adgang til at ændre denne institutions kutymefridage.'
const NOT_FOUND_TEXT = 'Dagen findes ikke længere. Genindlæs siden.'
const GENERIC_FAILURE = 'Ændringen kunne ikke gemmes. Prøv igen.'

/** The last date the screen sends. The server refuses 9999-12-31 as a start date (no later end date
 * exists; DATE_OUT_OF_RANGE has no Danish text) but accepts it as a stop or single date; the screen is
 * stricter and every date input stops one day short. */
const MAX_DATE = '9999-12-30'
/** A real calendar date written yyyy-mm-dd (a five-digit year is not one). */
function isRealDate(d: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(d)) return false
  const [y, m, day] = d.split('-').map(Number)
  const t = new Date(Date.UTC(y, m - 1, day))
  t.setUTCFullYear(y)
  return t.getUTCFullYear() === y && t.getUTCMonth() === m - 1 && t.getUTCDate() === day
}
/** A date the screen knows the server accepts: a real date, not before `min`, not after MAX_DATE. */
const dateAllowed = (d: string, min?: string) => isRealDate(d) && (!min || d >= min) && d <= MAX_DATE

/** A code to its Danish sentence; an unknown code gets the generic one, never the code or the English
 * error. `nearest` is the server's nearestValid[0] (BEFORE_WITHDRAWAL names the earliest date). */
export function customaryDayErrorText(code: string | undefined, nearest?: string): string {
  if (code === 'BEFORE_WITHDRAWAL') {
    return nearest ? `Dagen kan tidligst genindføres fra ${longDate(nearest)}.` : UNPLACEABLE_REFUSAL
  }
  return (code && CODE_TEXT[code]) || UNPLACEABLE_REFUSAL
}

// ── Dates ──

/** "2026-11-02" -> "2. november 2026" (the da-DK long form, without depending on ICU data). */
function longDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return `${d}. ${MONTHS[m - 1]} ${y}`
}

function addDays(iso: string, n: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + n)).toISOString().slice(0, 10)
}

function daysInMonth(month: number): number {
  // 2024 is a leap year: 29 February is a real recurring day (the server allows it).
  return new Date(Date.UTC(2024, month, 0)).getUTCDate()
}

function dayText(r: CustomaryDay): string {
  if (r.singleDate) return longDate(r.singleDate)
  return `Hvert år den ${r.recurringDay}. ${MONTHS[(r.recurringMonth ?? 1) - 1]}`
}

function partText(r: CustomaryDay): string {
  if (r.part === 'FROM_TIME') return `Fra kl. ${(r.partFromTime ?? '').slice(0, 5)}`
  if (r.part === 'HOURS') return `${formatHours(r.partHours ?? 0)} timer`
  return 'Hele dagen'
}

const labelOf = (opts: Option[], v: string | null) => opts.find(o => o.value === v)?.label ?? '—'

function sameDayAndScope(a: CustomaryDay, b: CustomaryDay): boolean {
  return a.singleDate === b.singleDate && a.recurringMonth === b.recurringMonth
    && a.recurringDay === b.recurringDay && a.agreementCode === b.agreementCode
}

/** The earliest date "Genindfør" may use, or null when it is not offered: a withdrawn row is
 * reinstated only when no other row of the same day and scope is open, and not before the latest
 * end among such rows (the server refuses an overlap and BEFORE_WITHDRAWAL). */
function reinstateMin(row: CustomaryDay, all: CustomaryDay[]): string | null {
  if (!row.validTo) return null
  const others = all.filter(o => o.customId !== row.customId && sameDayAndScope(o, row))
  if (others.some(o => !o.validTo)) return null
  return others.reduce((latest, o) => (o.validTo && o.validTo > latest ? o.validTo : latest), row.validTo)
}

// ── Refusals ──

interface FieldRefusal { field?: string; code?: string; nearestValid?: string[] | null }
interface RefusalBody { fields?: FieldRefusal[]; conflictingCustomId?: string }

function readRefusal(body: unknown): RefusalBody {
  if (!body || typeof body !== 'object') return {}
  const out: RefusalBody = {}
  if ('conflictingCustomId' in body && typeof body.conflictingCustomId === 'string') {
    out.conflictingCustomId = body.conflictingCustomId
  }
  if ('fields' in body && Array.isArray(body.fields)) {
    out.fields = body.fields.filter((f): f is FieldRefusal => !!f && typeof f === 'object')
  }
  return out
}

type Action = 'create' | 'withdraw' | 'reinstate'
type Refusal = { fields: Record<string, string>; text: string | null }

/** One refusal to Danish text: per-field messages keyed by the camel-cased field name, and one
 * sentence for the form's error area. Never the English `error`. */
function toRefusal(action: Action, status: number, body: unknown): Refusal {
  const b = readRefusal(body)
  if (status === 400 && b.fields && b.fields.length > 0) {
    const fields: Record<string, string> = {}
    for (const f of b.fields) {
      if (!f.field || f.field in fields) continue
      const key = f.field.charAt(0).toLowerCase() + f.field.slice(1)
      fields[key] = customaryDayErrorText(f.code, f.nearestValid?.[0])
    }
    return { fields, text: null }
  }
  let text = GENERIC_FAILURE
  if (status === 409) {
    text = action === 'withdraw' ? ALREADY_STOPPED_TEXT
      : action === 'reinstate' && !b.conflictingCustomId ? NOT_STOPPED_TEXT
      : OVERLAP_TEXT
  } else if (status === 403) text = FORBIDDEN_TEXT
  else if (status === 404) text = NOT_FOUND_TEXT
  return { fields: {}, text }
}

// ── Form ──

interface FormState {
  dayForm: '' | 'yearly' | 'single'
  month: string
  day: string
  date: string
  kind: string
  part: string
  time: string
  hours: string
  paidMode: string
  agreement: string
  validFrom: string
}
const EMPTY_FORM: FormState = {
  dayForm: '', month: '', day: '', date: '', kind: '', part: '', time: '', hours: '',
  paidMode: '', agreement: ALL_AGREEMENTS, validFrom: '',
}

function formComplete(f: FormState): boolean {
  const dayOk = f.dayForm === 'yearly' ? !!f.month && !!f.day : f.dayForm === 'single' && !!f.date
  const partOk = f.part === 'WHOLE_DAY' || (f.part === 'FROM_TIME' && !!f.time) || (f.part === 'HOURS' && Number(f.hours) > 0)
  const modeOk = f.kind === 'CLOSURE_DAY' || (f.kind === 'PAID_DAY_OFF' && !!f.paidMode)
  return dayOk && !!f.kind && partOk && modeOk && dateAllowed(f.validFrom)
    && (f.dayForm !== 'single' || dateAllowed(f.date))
}

/** True when a listed row already holds the draft's day for the same agreement scope and is open
 * or ends after the draft's start: the server would answer 409 (form-exact key, ruling 8). */
function duplicateListed(f: FormState, all: CustomaryDay[]): boolean {
  if (!f.validFrom) return false
  const yearly = f.dayForm === 'yearly' && !!f.month && !!f.day
  const single = f.dayForm === 'single' && !!f.date
  if (!yearly && !single) return false
  const scope = f.agreement === ALL_AGREEMENTS ? null : f.agreement
  return all.some(r => r.agreementCode === scope
    && (yearly
      ? r.recurringMonth === Number(f.month) && r.recurringDay === Number(f.day) && !r.singleDate
      : r.singleDate === f.date)
    && (!r.validTo || r.validTo > f.validFrom))
}

/** Only the chosen form's members are sent; the other form's are omitted. */
function toBody(f: FormState): CreateBody {
  return {
    agreementCode: f.agreement === ALL_AGREEMENTS ? null : f.agreement,
    ...(f.dayForm === 'yearly'
      ? { recurringMonth: Number(f.month), recurringDay: Number(f.day) }
      : { singleDate: f.date }),
    kind: f.kind,
    part: f.part,
    ...(f.part === 'FROM_TIME' ? { partFromTime: `${f.time.slice(0, 5)}:00` } : {}),
    ...(f.part === 'HOURS' ? { partHours: Number(f.hours) } : {}),
    ...(f.kind === 'PAID_DAY_OFF' ? { paidMode: f.paidMode } : {}),
    validFrom: f.validFrom,
  }
}

const errorStyle = { color: 'var(--color-error)', fontSize: '0.875rem' } as const
const rowStyle = { display: 'flex', gap: '0.75rem', flexWrap: 'wrap', alignItems: 'center' } as const
const fieldsetStyle = { border: 'none', padding: 0, margin: '0 0 1rem' } as const

function FieldError({ id, text }: { id: string; text?: string }) {
  return text ? <span id={id} role="alert" style={errorStyle}>{text}</span> : null
}

interface RadioGroupProps {
  legend: string
  name: string
  options: Option[]
  value: string
  onChange: (v: string) => void
  errorId: string
  error?: string
}

/** A `<fieldset>` with a `<legend>`; its refusal text is tied to the fieldset, and each radio input
 * of a refused group carries aria-invalid (the fieldset never does). */
function RadioGroup({ legend, name, options, value, onChange, errorId, error }: RadioGroupProps) {
  return (
    <fieldset style={fieldsetStyle} aria-describedby={error ? errorId : undefined}>
      <legend>{legend}</legend>
      <div style={rowStyle}>
        {options.map(o => (
          <Radio
            key={o.value}
            id={`${name}-${o.value}`}
            name={name}
            value={o.value}
            label={o.label}
            checked={value === o.value}
            invalid={!!error}
            onChange={() => onChange(o.value)}
          />
        ))}
      </div>
      <FieldError id={errorId} text={error} />
    </fieldset>
  )
}

// `session` is unique per open and `rev` counts date edits: a late answer is shown only while both
// still match the form on screen, so it never restores a draft the user has since changed or closed.
interface Inline {
  id: string; action: 'withdraw' | 'reinstate'; date: string; min: string; refusal: string | null
  session: number; rev: number
}

/** The earliest date the row's action may use now, or null when the row no longer offers it. */
function actionMin(row: CustomaryDay | undefined, action: Inline['action'], all: CustomaryDay[]): string | null {
  if (!row) return null
  if (action === 'withdraw') return row.validTo ? null : addDays(row.validFrom, 1)
  return reinstateMin(row, all)
}

/** The open inline action checked against a fresh list: closed when no longer offered, else its min recomputed. */
function reconcile(cur: Inline | null, all: CustomaryDay[]): Inline | null {
  if (!cur) return null
  const min = actionMin(all.find(r => r.customId === cur.id), cur.action, all)
  if (min === null) return null
  return min === cur.min ? cur : { ...cur, min }
}

export function CustomaryDaysEditor({ orgId, agreementOptions }: CustomaryDaysEditorProps) {
  const [rows, setRows] = useState<CustomaryDay[]>([])
  const [load, setLoad] = useState<'loading' | 'ok' | 'failed'>('loading')
  const [form, setForm] = useState<FormState>(EMPTY_FORM)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [status, setStatus] = useState('')
  const [inline, setInline] = useState<Inline | null>(null)
  const [busy, setBusy] = useState(false)
  const latest = useRef(0)
  // What a completion is checked against: the organisation shown now (null once unmounted) and the
  // row action being edited now. A result for anything else is not this screen's to show.
  const liveOrg = useRef<string | null>(orgId)
  const liveInline = useRef<Inline | null>(null)
  const inFlight = useRef(false)
  const sessions = useRef(0)
  liveInline.current = inline
  useEffect(() => {
    liveOrg.current = orgId
    return () => { liveOrg.current = null }
  }, [orgId])

  const refresh = useCallback(async () => {
    const request = ++latest.current
    const result = await apiClient.get('/api/config/{orgId}/customary-days', { params: { path: { orgId } } })
    if (request !== latest.current) return
    if (result.ok) { setRows(result.data); setLoad('ok'); setInline(cur => reconcile(cur, result.data)) } else setLoad('failed')
  }, [orgId])

  useEffect(() => {
    setLoad('loading')
    setInline(null)
    void refresh()
  }, [refresh])

  const set = (patch: Partial<FormState>) => setForm(f => ({ ...f, ...patch }))

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!formComplete(form) || saving || duplicateListed(form, rows)) return
    setSaving(true)
    setFieldErrors({})
    setFormError(null)
    setStatus('')
    const result = await apiFetchWithEtag('/api/config/{orgId}/customary-days', {
      method: 'POST',
      params: { path: { orgId } },
      body: toBody(form),
    })
    if (liveOrg.current !== orgId) return
    setSaving(false)
    if (result.ok) {
      setForm(EMPTY_FORM)
      setStatus('Dagen er gemt.')
      await refresh()
      return
    }
    const refusal = toRefusal('create', result.status, result.body)
    setFieldErrors(refusal.fields)
    setFormError(refusal.text)
  }

  async function confirm() {
    if (!inline || inFlight.current) return
    const min = actionMin(rows.find(r => r.customId === inline.id), inline.action, rows)
    if (min === null || !dateAllowed(inline.date, min)) return
    const { id, action, date, session, rev } = inline
    inFlight.current = true
    setBusy(true)
    const result = action === 'withdraw'
      ? await apiFetchWithEtag('/api/config/{orgId}/customary-days/{customId}/withdraw', {
        method: 'POST', params: { path: { orgId, customId: id } }, body: { validTo: date },
      })
      : await apiFetchWithEtag('/api/config/{orgId}/customary-days/{customId}/reinstate', {
        method: 'POST', params: { path: { orgId, customId: id } }, body: { validFrom: date },
      })
    inFlight.current = false
    if (liveOrg.current !== orgId) return
    setBusy(false)
    const same = liveInline.current?.session === session && liveInline.current.rev === rev
    if (result.ok) {
      if (!same) { await refresh(); return }
      setInline(null)
      setFormError(null)
      setStatus(action === 'withdraw' ? `Dagen ophører fra ${longDate(date)}.` : `Dagen er genindført fra ${longDate(date)}.`)
      await refresh()
      return
    }
    if (!same) return
    const refusal = toRefusal(action, result.status, result.body)
    const first = Object.values(refusal.fields)[0]
    setStatus('')
    setInline({ ...inline, refusal: refusal.text ?? first ?? GENERIC_FAILURE })
  }

  // Controls shown now; a refusal naming a hidden control is shown in the form's error area instead.
  const shown = new Set(['agreementCode', 'validFrom', 'kind', 'part'])
  if (form.dayForm === 'yearly') { shown.add('recurringMonth'); shown.add('recurringDay') }
  shown.add('singleDate')
  if (form.part === 'FROM_TIME') shown.add('partFromTime')
  if (form.part === 'HOURS') shown.add('partHours')
  if (form.kind === 'PAID_DAY_OFF') shown.add('paidMode')
  const unplaced = Object.entries(fieldErrors).filter(([k]) => !shown.has(k)).map(([, t]) => t)
  const errId = (k: string) => `cd-err-${k}`
  const err = (k: string) => fieldErrors[k]
  const inputProps = (k: string) => (err(k) ? { error: true, 'aria-describedby': errId(k) } : {})
  const maxDay = form.month ? daysInMonth(Number(form.month)) : 31
  const duplicate = duplicateListed(form, rows)
  const dayOptions: Option[] = Array.from({ length: maxDay }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }))

  return (
    <Card header={<h2 style={{ margin: 0 }}>Kutymefridage og lukkedage</h2>}>
      <p>
        Fridage og lukkedage, som institutionen selv har fastsat, for eksempel grundlovsdag fra kl. 12
        eller juleaftensdag. De gælder for hele institutionen, uanset den valgte overenskomst og OK-version.
      </p>
      <p>{D6_NOTE}</p>

      <p role="status" style={{ margin: '0.5rem 0' }}>{status}</p>

      {load === 'loading' && <Spinner />}
      {load === 'failed' && <Alert variant="error">Kutymefridagene kunne ikke hentes. Prøv igen.</Alert>}
      {load === 'ok' && rows.length === 0 && (
        <p>Der er ikke registreret kutymefridage eller lukkedage for institutionen.</p>
      )}
      {load === 'ok' && rows.length > 0 && (
        <Table headers={HEADERS}>
          {rows.map(r => {
            const open = !r.validTo
            const min = open ? addDays(r.validFrom, 1) : reinstateMin(r, rows)
            const here = inline?.id === r.customId ? inline : null
            return (
              <tr key={r.customId}>
                <td id={`cd-day-${r.customId}`}>{dayText(r)}</td>
                <td>{labelOf(KIND_OPTIONS, r.kind)}</td>
                <td>{partText(r)}</td>
                <td>{labelOf(MODE_OPTIONS, r.paidMode)}</td>
                <td>{r.agreementCode ?? 'Alle overenskomster'}</td>
                <td>{longDate(r.validFrom)}</td>
                <td>{r.validTo ? longDate(r.validTo) : '—'}</td>
                <td>
                  {here ? (
                    <div style={rowStyle}>
                      <label id={`cd-lbl-${r.customId}`} htmlFor={`cd-date-${r.customId}`}>
                        {here.action === 'withdraw' ? 'Ophører fra' : 'Genindføres fra'}
                      </label>
                      <Input
                        id={`cd-date-${r.customId}`}
                        type="date"
                        aria-labelledby={`cd-lbl-${r.customId} cd-day-${r.customId}`}
                        aria-describedby={here.refusal ? `cd-inline-err-${r.customId}` : undefined}
                        error={!!here.refusal}
                        min={here.min}
                        max={MAX_DATE}
                        value={here.date}
                        onChange={e => setInline({ ...here, date: e.target.value, refusal: null, rev: here.rev + 1 })}
                      />
                      <Button
                        size="sm"
                        disabled={!dateAllowed(here.date, here.min) || busy}
                        aria-busy={busy}
                        onClick={() => void confirm()}
                      >
                        {here.action === 'withdraw' ? 'Afslut' : 'Genindfør'}
                      </Button>
                      <Button size="sm" variant="secondary" onClick={() => setInline(null)}>Annuller</Button>
                      <FieldError id={`cd-inline-err-${r.customId}`} text={here.refusal ?? undefined} />
                    </div>
                  ) : min ? (
                    <Button
                      size="sm"
                      variant="secondary"
                      aria-describedby={`cd-day-${r.customId}`}
                      onClick={() => setInline({
                        id: r.customId, action: open ? 'withdraw' : 'reinstate', date: '', min, refusal: null,
                        session: ++sessions.current, rev: 0,
                      })}
                    >
                      {open ? 'Afslut' : 'Genindfør'}
                    </Button>
                  ) : null}
                </td>
              </tr>
            )
          })}
        </Table>
      )}

      <form onSubmit={e => void submit(e)} aria-labelledby="cd-form-heading" style={{ marginTop: '1.5rem' }}>
        <h3 id="cd-form-heading">Tilføj kutymefridag eller lukkedag</h3>

        <RadioGroup
          legend="Hvornår" name="cd-dayform" options={DAY_FORM_OPTIONS} value={form.dayForm}
          onChange={v => set({ dayForm: v === 'single' ? 'single' : 'yearly' })}
          errorId={errId('singleDate')} error={err('singleDate')}
        />
        {form.dayForm === 'yearly' && (
          <div style={rowStyle}>
            <FormField label="Måned" htmlFor="cd-month">
              <Select
                id="cd-month" options={MONTH_OPTIONS} value={form.month} placeholder="Vælg..."
                invalid={!!err('recurringMonth')} describedBy={err('recurringMonth') ? errId('recurringMonth') : undefined}
                onValueChange={v => set({ month: v, day: Number(form.day) > daysInMonth(Number(v)) ? '' : form.day })}
              />
              <FieldError id={errId('recurringMonth')} text={err('recurringMonth')} />
            </FormField>
            <FormField label="Dag" htmlFor="cd-day">
              <Select
                id="cd-day" options={dayOptions} value={form.day} placeholder="Vælg..."
                invalid={!!err('recurringDay')} describedBy={err('recurringDay') ? errId('recurringDay') : undefined}
                onValueChange={v => set({ day: v })}
              />
              <FieldError id={errId('recurringDay')} text={err('recurringDay')} />
            </FormField>
          </div>
        )}
        {form.dayForm === 'single' && (
          <FormField label="Dato" htmlFor="cd-date">
            <Input
              id="cd-date" type="date" max={MAX_DATE} value={form.date}
              onChange={e => set({ date: e.target.value })} {...inputProps('singleDate')}
            />
          </FormField>
        )}

        <RadioGroup
          legend="Type" name="cd-kind" options={KIND_OPTIONS} value={form.kind}
          onChange={v => set({ kind: v, paidMode: v === 'PAID_DAY_OFF' ? form.paidMode : '' })}
          errorId={errId('kind')} error={err('kind')}
        />

        <RadioGroup
          legend="Omfang" name="cd-part" options={PART_OPTIONS} value={form.part}
          onChange={v => set({ part: v, time: '', hours: '' })}
          errorId={errId('part')} error={err('part')}
        />
        {form.part === 'FROM_TIME' && (
          <FormField label="Fri fra kl." htmlFor="cd-time">
            <Input
              id="cd-time" type="time" step={60} value={form.time}
              onChange={e => set({ time: e.target.value })} {...inputProps('partFromTime')}
            />
            <FieldError id={errId('partFromTime')} text={err('partFromTime')} />
          </FormField>
        )}
        {form.part === 'HOURS' && (
          <FormField label="Antal timer (ved fuld tid)" htmlFor="cd-hours">
            <Input
              id="cd-hours" type="number" min={0.01} max={99.99} step={0.01} value={form.hours}
              onChange={e => set({ hours: e.target.value })} {...inputProps('partHours')}
            />
            <FieldError id={errId('partHours')} text={err('partHours')} />
          </FormField>
        )}

        {form.kind === 'PAID_DAY_OFF' && (
          <RadioGroup
            legend="Tildeling" name="cd-mode" options={MODE_OPTIONS} value={form.paidMode}
            onChange={v => set({ paidMode: v })} errorId={errId('paidMode')} error={err('paidMode')}
          />
        )}

        <FormField label="Gælder for" htmlFor="cd-agreement">
          <Select
            id="cd-agreement" value={form.agreement} onValueChange={v => set({ agreement: v })}
            invalid={!!err('agreementCode')} describedBy={err('agreementCode') ? errId('agreementCode') : undefined}
            options={[{ value: ALL_AGREEMENTS, label: 'Alle overenskomster' }, ...agreementOptions]}
          />
          <FieldError id={errId('agreementCode')} text={err('agreementCode')} />
        </FormField>

        <FormField label="Gælder fra" htmlFor="cd-valid-from">
          <Input
            id="cd-valid-from" type="date" max={MAX_DATE} value={form.validFrom}
            onChange={e => set({ validFrom: e.target.value })} {...inputProps('validFrom')}
          />
          <FieldError id={errId('validFrom')} text={err('validFrom')} />
        </FormField>

        {(formError || unplaced.length > 0) && (
          <Alert variant="error">{formError ?? unplaced.join(' ')}</Alert>
        )}

        {duplicate && <p id="cd-overlap" role="status" style={errorStyle}>{OVERLAP_TEXT}</p>}

        <Button
          type="submit"
          disabled={!formComplete(form) || saving || duplicate}
          aria-busy={saving}
          aria-describedby={duplicate ? 'cd-overlap' : undefined}
        >
          {saving ? 'Gemmer...' : 'Tilføj'}
        </Button>
      </form>
    </Card>
  )
}
