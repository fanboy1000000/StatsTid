// The HR section of the edit-person drawer: the two HR-recorded facts, the overtime regime and
// membership of the flex scheme (TASK-14836's routes). It loads and saves on its own, with its own
// date and button; the drawer's "Gem ændringer" neither sends nor waits for it, and its failures
// never reach the drawer's error area. The server is the authority on the values (the allowed set
// per agreement, read for the form's date) and on the dates; this screen only offers what the server
// serves and shows each refusal in Danish. No calculation reads either fact yet, and the section
// says so (D6_NOTE). The seeded "since always" row (0001-01-01) is never shown and sets no earliest date.
import { useCallback, useEffect, useRef, useState } from 'react'
import { apiFetchWithEtag, type ApiResult, type ApiResponseWithEtag } from '../../../lib/api'
import type { components } from '../../../lib/api-types'
import { formatVersionAsIfMatch, parseVersionFromETag } from '../../../lib/etag'
import { CODE_MESSAGES, D6_NOTE, UNPLACEABLE_REFUSAL } from '../../../components/config/profileSettingsCopy'
import { Badge } from '../../../components/ui'
import styles from '../EditPersonDrawer.module.css'

type HrFacts = components['schemas']['StatsTid.Backend.Api.Contracts.EmployeeHrFactsResponse']

interface HrFactsSectionProps {
  employeeId: string
  /** The server-confirmed Copenhagen day; null when unresolvable (the drawer already says so). */
  today: string | null
  disabled?: boolean
  /** Id of the drawer's self-lock note: set on the logged-in user's own record, which locks the form. */
  selfLockNoteId?: string
}

const SEEDED_ANCHOR = '0001-01-01'
const MISSING = 'Mangler – ikke registreret'

const REGIME_LABELS: Record<string, string> = {
  OVERTIME: 'Overarbejde: ret til overarbejdsbetaling',
  EXEMPT: 'Undtaget fra reglerne om overarbejde',
  CONTRACTUAL: 'Merarbejde: krav på godtgørelse',
  DISCRETIONARY: 'Merarbejde: godtgørelse efter skøn',
  NONE: 'Ingen godtgørelse for merarbejde',
}

function longDate(iso: string): string {
  return new Date(`${iso}T00:00:00Z`).toLocaleDateString('da-DK', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  })
}

/** A server refusal code as Danish text; never the raw code or a body's English. */
export function hrFactsCodeText(code: string, nearestValid?: string[] | null): string {
  switch (code) {
    case 'UNKNOWN_VALUE':
      return CODE_MESSAGES.UNKNOWN_VALUE
    case 'NOT_ALLOWED_FOR_AGREEMENT':
      return 'Værdien kan ikke vælges for den overenskomst, medarbejderen har på den valgte dato.'
    case 'EFFECTIVE_FROM_NOT_TODAY_OR_PAST':
      return 'Datoen kan ikke ligge efter i dag.'
    case 'BEFORE_CURRENT_ROW':
      return nearestValid?.[0] ? `Datoen kan tidligst være ${longDate(nearestValid[0])}.` : UNPLACEABLE_REFUSAL
    default:
      return UNPLACEABLE_REFUSAL
  }
}

const CHANGED = 'Oplysningerne er ændret i mellemtiden. De er indlæst igen; kontrollér dem, og gem igen.'
const NO_ACCESS_SAVE = 'Du har ikke adgang til at ændre disse oplysninger.'
const NO_ACCESS_READ = 'Du har ikke adgang til at se disse oplysninger.'

type FieldKey = 'regime' | 'flex' | 'date'
type FieldErrors = Partial<Record<FieldKey, string>>

function fieldKeyOf(field: string): FieldKey | null {
  const f = field.toLowerCase()
  if (f.includes('regime')) return 'regime'
  if (f.includes('flex')) return 'flex'
  if (f.includes('effective')) return 'date'
  return null
}

const describedBy = (...ids: (string | undefined | false)[]) => ids.filter(Boolean).join(' ') || undefined

export function HrFactsSection({ employeeId, today, disabled = false, selfLockNoteId }: HrFactsSectionProps) {
  const [current, setCurrent] = useState<HrFacts | null>(null)
  const [etag, setEtag] = useState<string | null>(null)
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'failed' | 'noAccess'>('loading')
  // The agreement and allowed set the server serves for the form's date.
  const [options, setOptions] = useState<{ agreementCode: string | null; allowed: string[] }>({
    agreementCode: null,
    allowed: [],
  })
  const [regime, setRegime] = useState('')
  const [flex, setFlex] = useState<'' | 'true' | 'false'>('')
  const [date, setDate] = useState(today ?? '')
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const optionsSeq = useRef(0)

  const readFacts = useCallback(
    (asOf: string): Promise<ApiResult<ApiResponseWithEtag<HrFacts>>> =>
      apiFetchWithEtag('/api/admin/employees/{employeeId}/hr-facts', {
        method: 'GET',
        params: { path: { employeeId } },
        query: { asOf },
      }),
    [employeeId],
  )

  // Reads today's facts, which also carry the version the next save must name, and resets the form to them.
  const reload = useCallback(
    async (isCancelled: () => boolean = () => false): Promise<boolean> => {
      if (today == null) return false
      const result = await readFacts(today)
      if (isCancelled()) return false
      if (!result.ok) {
        setLoadState(result.status === 403 ? 'noAccess' : 'failed')
        return false
      }
      const facts = result.data.data
      setCurrent(facts)
      setEtag(result.data.etag)
      setOptions({ agreementCode: facts.agreementCode ?? null, allowed: facts.allowedOvertimeRegimes })
      setRegime(facts.overtimeRegime ?? '')
      setFlex(facts.onFlexScheme == null ? '' : facts.onFlexScheme ? 'true' : 'false')
      setDate(today)
      setLoadState('ready')
      return true
    },
    [readFacts, today],
  )

  useEffect(() => {
    let cancelled = false
    setLoadState('loading')
    setMessage(null)
    setFieldErrors({})
    void reload(() => cancelled)
    return () => {
      cancelled = true
    }
  }, [reload])

  const onDateChange = async (next: string) => {
    setDate(next)
    setFieldErrors((e) => ({ ...e, date: undefined }))
    if (!/^\d{4}-\d{2}-\d{2}$/.test(next)) return
    const seq = ++optionsSeq.current
    const result = await readFacts(next)
    if (seq !== optionsSeq.current || !result.ok) return
    const allowed = result.data.data.allowedOvertimeRegimes
    setOptions({ agreementCode: result.data.data.agreementCode ?? null, allowed })
    setRegime((r) => (allowed.includes(r) ? r : ''))
  }

  const save = async () => {
    setSaving(true)
    setMessage(null)
    setFieldErrors({})
    const version = current?.liveVersion ?? parseVersionFromETag(etag)
    const body = { effectiveFrom: date, overtimeRegime: regime || null, onFlexScheme: flex === 'true' }
    const result =
      version == null
        ? await apiFetchWithEtag('/api/admin/employees/{employeeId}/hr-facts', {
            method: 'PUT',
            params: { path: { employeeId } },
            ifNoneMatch: '*',
            body,
          })
        : await apiFetchWithEtag('/api/admin/employees/{employeeId}/hr-facts', {
            method: 'PUT',
            params: { path: { employeeId } },
            ifMatch: formatVersionAsIfMatch(version),
            body,
          })
    if (result.ok) {
      const reloaded = await reload()
      setMessage(reloaded ? { text: 'Oplysningerne om overarbejde og flex er gemt.', error: false } : null)
    } else if (result.status === 400) {
      const fields = (result.body as { fields?: { field: string; code: string; nearestValid?: string[] | null }[] } | undefined)
        ?.fields
      const errors: FieldErrors = {}
      let unplaced = fields == null || fields.length === 0
      for (const f of fields ?? []) {
        const key = fieldKeyOf(f.field)
        if (key == null) unplaced = true
        else errors[key] ??= hrFactsCodeText(f.code, f.nearestValid)
      }
      setFieldErrors(errors)
      if (unplaced) setMessage({ text: UNPLACEABLE_REFUSAL, error: true })
    } else if (result.status === 412 || (result.status === 409 && (result.body as { currentVersion?: number } | undefined)?.currentVersion != null)) {
      await reload()
      setMessage({ text: CHANGED, error: true })
    } else if (result.status === 409) {
      setMessage({
        text: 'Medarbejderen er flyttet til en anden institution i mellemtiden. Genindlæs siden.',
        error: true,
      })
    } else if (result.status === 403) {
      setMessage({ text: NO_ACCESS_SAVE, error: true })
    } else if (result.status === 404) {
      setMessage({ text: 'Medarbejderen findes ikke længere.', error: true })
    } else {
      setMessage({ text: UNPLACEABLE_REFUSAL, error: true })
    }
    setSaving(false)
  }

  const locked = !!selfLockNoteId
  const inputsOff = disabled || locked || saving || loadState !== 'ready'
  const recorded = current?.overtimeRegime ?? null
  const seeded = current?.liveEffectiveFrom == null || current.liveEffectiveFrom === SEEDED_ANCHOR
  const min = seeded ? undefined : (current?.liveEffectiveFrom ?? undefined)
  const noAgreement = options.agreementCode == null
  const canSave = !inputsOff && flex !== '' && date !== '' && !(recorded != null && regime === '')

  return (
    <section className={styles.section} aria-labelledby="pd-hrf-heading">
      <h3 id="pd-hrf-heading" className={styles.sectionLabel}>
        Overarbejde, merarbejde og flex
      </h3>
      <div className={styles.helperText} id="pd-hrf-note">
        {D6_NOTE}
      </div>

      <div role="status" data-testid="pd-hrf-status">
        {loadState === 'failed' && 'Oplysningerne kunne ikke hentes. Prøv igen.'}
        {loadState === 'noAccess' && NO_ACCESS_READ}
        {message && (
          <div className={message.error ? styles.sectionError : undefined} data-testid="pd-hrf-message">
            {message.text}
          </div>
        )}
      </div>

      {current && (
        <>
          <div className={styles.formField} data-testid="pd-hrf-current">
            <div className={styles.staticValue}>
              {recorded ? (
                <>
                  <span data-testid="pd-hrf-current-regime">{REGIME_LABELS[recorded] ?? recorded}</span>
                  {current.effectiveFrom && current.effectiveFrom !== SEEDED_ANCHOR && (
                    <> · fra {longDate(current.effectiveFrom)}</>
                  )}
                </>
              ) : (
                <span data-testid="pd-hrf-current-regime">
                  <Badge variant="warning">{MISSING}</Badge>
                </span>
              )}
            </div>
            <div className={styles.staticValue} data-testid="pd-hrf-current-flex">
              Omfattet af flexordningen:{' '}
              {current.onFlexScheme == null ? 'Ikke registreret' : current.onFlexScheme ? 'Ja' : 'Nej'}
            </div>
          </div>

          <div className={styles.formField}>
            <label className={styles.formLabel} htmlFor="pd-hrf-regime">
              Ordning for over- og merarbejde
            </label>
            <select
              className={styles.select}
              id="pd-hrf-regime"
              value={regime}
              onChange={(e) => {
                setRegime(e.target.value)
                setFieldErrors((f) => ({ ...f, regime: undefined }))
              }}
              disabled={inputsOff || noAgreement}
              aria-invalid={fieldErrors.regime ? true : undefined}
              aria-describedby={describedBy(fieldErrors.regime && 'pd-hrf-regime-error', selfLockNoteId)}
              data-testid="pd-hrf-regime"
            >
              {recorded == null && <option value="">{MISSING}</option>}
              {!noAgreement &&
                options.allowed.map((code) => (
                  <option key={code} value={code}>
                    {REGIME_LABELS[code] ?? code}
                  </option>
                ))}
            </select>
            {fieldErrors.regime && (
              <div id="pd-hrf-regime-error" className={styles.sectionError}>
                {fieldErrors.regime}
              </div>
            )}
            {noAgreement && (
              <div className={styles.helperText} data-testid="pd-hrf-no-agreement">
                Medarbejderen har ingen overenskomst på den valgte dato, så en ordning kan ikke vælges.
              </div>
            )}
          </div>

          <fieldset className={styles.formField} style={{ border: 0, padding: 0, margin: 0 }}>
            <legend className={styles.formLabel}>Omfattet af flexordningen</legend>
            {(['true', 'false'] as const).map((v) => (
              <label key={v} className={styles.checkboxRow} htmlFor={`pd-hrf-flex-${v}`}>
                <input
                  type="radio"
                  id={`pd-hrf-flex-${v}`}
                  name="pd-hrf-flex"
                  checked={flex === v}
                  onChange={() => {
                    setFlex(v)
                    setFieldErrors((f) => ({ ...f, flex: undefined }))
                  }}
                  disabled={inputsOff}
                  aria-invalid={fieldErrors.flex ? true : undefined}
                  aria-describedby={describedBy(fieldErrors.flex && 'pd-hrf-flex-error', selfLockNoteId)}
                  data-testid={`pd-hrf-flex-${v === 'true' ? 'yes' : 'no'}`}
                />
                <span>{v === 'true' ? 'Ja' : 'Nej'}</span>
              </label>
            ))}
            {fieldErrors.flex && (
              <div id="pd-hrf-flex-error" className={styles.sectionError}>
                {fieldErrors.flex}
              </div>
            )}
          </fieldset>

          <div className={styles.formField}>
            <label className={styles.formLabel} htmlFor="pd-hrf-date">
              Gælder fra
            </label>
            <input
              className={styles.input}
              id="pd-hrf-date"
              type="date"
              value={date}
              max={today ?? undefined}
              min={min}
              onChange={(e) => void onDateChange(e.target.value)}
              disabled={inputsOff}
              aria-invalid={fieldErrors.date ? true : undefined}
              aria-describedby={describedBy('pd-hrf-date-help', fieldErrors.date && 'pd-hrf-date-error', selfLockNoteId)}
              data-testid="pd-hrf-date"
            />
            <div className={styles.helperText} id="pd-hrf-date-help">
              {min
                ? `Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag og ikke før ${longDate(min)}, hvor den nuværende registrering begynder.`
                : 'Dagens dato er forvalgt. Datoen kan ikke ligge efter i dag.'}
            </div>
            {fieldErrors.date && (
              <div id="pd-hrf-date-error" className={styles.sectionError}>
                {fieldErrors.date}
              </div>
            )}
          </div>

          <button
            type="button"
            className={styles.submitBtn}
            onClick={() => void save()}
            disabled={!canSave}
            aria-describedby={selfLockNoteId}
            data-testid="pd-hrf-save"
          >
            {saving ? 'Gemmer...' : 'Gem overarbejde og flex'}
          </button>
        </>
      )}
    </section>
  )
}
