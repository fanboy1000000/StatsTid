// Local agreement profile editor (S21 / ADR-017 / TASK-2109).
//
// One editor instance per (org, agreement, OkVersion). Renders:
//   - A header with profile metadata + a disabled "deactivate" button
//     (no DELETE endpoint yet — see deferred-items note below).
//   - Four overridable fields with an "Override central" toggle. When the
//     toggle is OFF the central value is shown read-only and the saved
//     value is NULL ("inherit central"). When ON the user types a value
//     and that value is what gets PUT.
//   - A second card with the institution's own flex, office-hours and paid-absence-credit
//     settings (ADR-043 D6): nine fields, each with a note that nothing reads them yet, an
//     office-hours warning that never blocks the save, and AC's "default by analogy" label.
//     Their Danish copy lives in profileSettingsCopy.ts.
//   - The remaining ~21 central agreement-config fields rendered read-only
//     ("see what you can't override"). These come from the central agreement
//     config and are NOT part of the profile schema.
//   - An effective-from date picker. mondayOnly switches dynamically based on
//     whether WeeklyNormHours is in the changed-fields set; pastOrTodayOnly is
//     always true.
//   - A Save button that's disabled until at least one field has changed
//     vs the loaded profile (or vs central if no profile exists). On click,
//     the editor PUTs its full state (23 keys); the server clears anything it omits.
//   - 412 stale-state banner + 400 per-field error rendering.
//
// SCOPE: basic functional only. No visual polish — Phase 5 owns that. The
// component intentionally uses `useState` only; no global state, no form
// library.
import { useEffect, useMemo, useState } from 'react'
import { Button, Card, Alert, FormField, Input, Checkbox, Radio, Label } from '../ui'
import { saveProfile, type LocalAgreementProfile, type ProfileFieldError } from '../../hooks/useConfig'
import type { ProfileSaveRequest } from '../../api/profileApi'
import type { AgreementConfig } from '../../hooks/useAgreementConfigs'
import { MondayDatePicker } from './MondayDatePicker'
import {
  CAP_LABELS,
  CARD_HEADING,
  CHOICE_SPECS,
  CODE_MESSAGES,
  D6_NOTE,
  D8B_LABEL,
  FIELDS_BEFORE_OFFICE_HOURS,
  KONTORTID_HINT,
  OFFICE_HOURS_LEGEND,
  OFFICE_HOURS_MEMBERS,
  WEEKDAYS,
  isChoiceMember,
  officeHoursWarning,
  type CapMember,
  type ChoiceMember,
  type OfficeHoursMember,
  type SettingMember,
  UNPLACEABLE_REFUSAL,
} from './profileSettingsCopy'

// ── Field metadata. ──
//
// The note on the two fields whose only reader is the overtime-governance warning, which still
// uses the agreement's built-in values (R-8, until stage 2). Weekly norm and flex cap reach pay.
const NOT_IN_PAY_NOTE =
  'Påvirker endnu ikke lønberegningen. Advarslerne om overarbejde bruger stadig aftalens indbyggede værdi.'

// The overridable fields with an "Override central" toggle — they mirror the backend's
// ProfileSaveRequest / LocalAgreementProfile. A new one requires schema + alignment-policy
// changes upstream; this list is the UI side of that contract. The institution's own settings
// (profileSettingsCopy.ts) have no toggle: "not set" is their inherit.
type OverridableNumberField = 'weeklyNormHours' | 'maxFlexBalance' | 'maxOvertimeHoursPerPeriod'
type OverridableBoolField = 'overtimeRequiresPreApproval'

const OVERRIDABLE_NUMBER_FIELDS: { key: OverridableNumberField; label: string; backendName: string; note?: string }[] = [
  { key: 'weeklyNormHours',           label: 'Ugentlig normtimer',         backendName: 'WeeklyNormHours' },
  { key: 'maxFlexBalance',            label: 'Maks. flexsaldo',            backendName: 'MaxFlexBalance' },
  { key: 'maxOvertimeHoursPerPeriod', label: 'Maks. overarbejde pr periode', backendName: 'MaxOvertimeHoursPerPeriod', note: NOT_IN_PAY_NOTE },
]

const OVERRIDABLE_BOOL_FIELDS: { key: OverridableBoolField; label: string; backendName: string; note?: string }[] = [
  { key: 'overtimeRequiresPreApproval', label: 'Overarbejde kraever forhaandsgodkendelse', backendName: 'OvertimeRequiresPreApproval', note: NOT_IN_PAY_NOTE },
]

// Read-only central fields — every AgreementConfig field that is NOT one of the
// 4 overridable ones, minus the bookkeeping fields (configId, status, dates,
// etc.). The labels are Danish and intentionally minimal.
interface ReadOnlyFieldSpec {
  key: keyof AgreementConfig
  label: string
}

const READ_ONLY_FIELDS: ReadOnlyFieldSpec[] = [
  { key: 'normPeriodWeeks',          label: 'Normperiode (uger)' },
  { key: 'normModel',                label: 'Normmodel' },
  { key: 'annualNormHours',          label: 'Aarlig normtimer' },
  { key: 'hasOvertime',              label: 'Har overarbejde' },
  { key: 'hasMerarbejde',            label: 'Har merarbejde' },
  { key: 'overtimeThreshold50',      label: 'Overarbejdsgraense 50%' },
  { key: 'overtimeThreshold100',     label: 'Overarbejdsgraense 100%' },
  { key: 'eveningSupplementEnabled', label: 'Aftentillaeg aktivt' },
  { key: 'nightSupplementEnabled',   label: 'Nattillaeg aktivt' },
  { key: 'weekendSupplementEnabled', label: 'Weekendtillaeg aktivt' },
  { key: 'holidaySupplementEnabled', label: 'Helligdagstillaeg aktivt' },
  { key: 'eveningStart',             label: 'Aftenstart (time)' },
  { key: 'eveningEnd',               label: 'Aftenslut (time)' },
  { key: 'nightStart',               label: 'Natstart (time)' },
  { key: 'nightEnd',                 label: 'Natslut (time)' },
  { key: 'eveningRate',              label: 'Aftenrate' },
  { key: 'nightRate',                label: 'Natrate' },
  { key: 'weekendSaturdayRate',      label: 'Weekendrate (loerdag)' },
  { key: 'weekendSundayRate',        label: 'Weekendrate (soendag)' },
  { key: 'holidayRate',              label: 'Helligdagsrate' },
  { key: 'onCallDutyEnabled',        label: 'Raadighedsvagt aktiv' },
  { key: 'onCallDutyRate',           label: 'Raadighedsrate' },
]

// ── State shape. ──
//
// Per-field draft = "is the override on" + the typed value. NULL value with
// override OFF means inherit. NULL value with override ON means the user
// turned it on but hasn't entered anything yet (treated as still-inherited).
interface NumberDraft {
  enabled: boolean
  text: string  // raw input value; parsed on save
}

interface BoolDraft {
  enabled: boolean
  value: boolean
}

interface EditorState {
  effectiveFrom: string  // yyyy-MM-dd
  numbers: Record<OverridableNumberField, NumberDraft>
  bools: Record<OverridableBoolField, BoolDraft>
  // The institution's own settings as raw input text: '' = not set, a cap as typed, a clock time
  // as "HH:mm", a choice as its vocabulary value.
  settings: Record<SettingMember, string>
}

const CAP_MEMBERS: CapMember[] = ['flexPlusCapHours', 'flexMinusCapHours', 'flexDailyCreditCapHours']
const ALL_SETTING_MEMBERS: SettingMember[] = [
  ...(Object.keys(CHOICE_SPECS) as ChoiceMember[]),
  ...CAP_MEMBERS,
  ...OFFICE_HOURS_MEMBERS,
]

// The server's names for the 23 fields the editor sends and renders; a refusal naming anything
// else cannot be placed under a control.
const KNOWN_ERROR_FIELDS = new Set<string>([
  'EffectiveFrom',
  ...OVERRIDABLE_NUMBER_FIELDS.map(f => f.backendName),
  ...OVERRIDABLE_BOOL_FIELDS.map(f => f.backendName),
  ...ALL_SETTING_MEMBERS.map(m => m.charAt(0).toUpperCase() + m.slice(1)),
])

interface ProfileEditorProps {
  orgId: string
  agreementCode: string
  okVersion: string
  orgLabel: string  // human-readable org name for the header
  profile: LocalAgreementProfile | null
  etag: string | null
  centralConfig: AgreementConfig | null  // ACTIVE central config for this (agreement, OkVersion)
  loading: boolean
  loadError: string | null
  onSaved: () => void  // called after a successful save so the parent can refetch
}

export function ProfileEditor({
  orgId,
  agreementCode,
  okVersion,
  orgLabel,
  profile,
  etag,
  centralConfig,
  loading,
  loadError,
  onSaved,
}: ProfileEditorProps) {
  // ── Initial draft from loaded profile (or empty for first creation). ──
  const initialState = useMemo<EditorState>(() => buildInitialState(profile), [profile])
  const [state, setState] = useState<EditorState>(initialState)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  // S22 / ADR-018 D7: 412 body is `{ expectedVersion, actualVersion, currentState }`.
  // Carry the actualVersion so the stale-state banner can show it.
  const [staleConflict, setStaleConflict] = useState<{ expected?: number; actual?: number } | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, ProfileFieldError>>({})

  // Reset draft when the loaded profile changes (e.g. after a save → refresh).
  useEffect(() => {
    setState(initialState)
    setSaveError(null)
    setStaleConflict(null)
    setFieldErrors({})
  }, [initialState])

  // ── Derived: which overridable values changed vs the loaded profile. ──
  // The backend treats "no change" as "no override needed" — only changed
  // fields go through alignment validation, and only changed fields appear in
  // the audit delta. Match that semantics here for the disabled-until-changed
  // gate.
  const changedFieldNames = useMemo(() => {
    return computeChangedFieldNames(profile, state)
  }, [profile, state])

  const effectiveFromChangedVsLoaded = profile
    ? state.effectiveFrom !== profile.effectiveFrom
    : state.effectiveFrom !== ''  // for first creation, having any value is a change vs "nothing"

  const hasAnyChange = changedFieldNames.length > 0 || effectiveFromChangedVsLoaded
  const weeklyNormHoursIsChanged = changedFieldNames.includes('WeeklyNormHours')

  // The office-hours warning and the "Kontortid" gate read the draft (never the loaded profile).
  const hasWindow = WEEKDAYS.some(d => state.settings[`officeHours${d.key}Start`] !== '' && state.settings[`officeHours${d.key}End`] !== '')
  const officeSumMinutes = officeHoursMinutes(state.settings)
  const weekNorm = pickNumber(state.numbers.weeklyNormHours)
  const normHours = weekNorm !== null && weekNorm > 0 ? weekNorm : 37
  const officeWarning = officeSumMinutes !== null && officeSumMinutes !== Math.round(normHours * 60)
    ? officeHoursWarning(officeSumMinutes, normHours)
    : ''

  function updateSetting(member: SettingMember, text: string) {
    setState(prev => ({ ...prev, settings: { ...prev.settings, [member]: text } }))
  }
  function updateNumber(key: OverridableNumberField, patch: Partial<NumberDraft>) {
    setState(prev => ({
      ...prev,
      numbers: { ...prev.numbers, [key]: { ...prev.numbers[key], ...patch } },
    }))
  }
  function updateBool(key: OverridableBoolField, patch: Partial<BoolDraft>) {
    setState(prev => ({
      ...prev,
      bools: { ...prev.bools, [key]: { ...prev.bools[key], ...patch } },
    }))
  }

  async function handleSave() {
    if (!hasAnyChange || saving) return
    if (!state.effectiveFrom) {
      setSaveError('Vaelg en gaeldende-fra dato.')
      return
    }

    setSaving(true)
    setSaveError(null)
    setStaleConflict(null)
    setFieldErrors({})

    // Build the PUT body. Per the backend contract, NULL = inherit central;
    // any non-null value is the override. We pass the FULL profile state
    // (not a delta) — the backend computes the delta vs the predecessor.
    const body = {
      effectiveFrom: state.effectiveFrom,
      weeklyNormHours: pickNumber(state.numbers.weeklyNormHours),
      maxFlexBalance: pickNumber(state.numbers.maxFlexBalance),
      maxOvertimeHoursPerPeriod: pickNumber(state.numbers.maxOvertimeHoursPerPeriod),
      overtimeRequiresPreApproval: pickBool(state.bools.overtimeRequiresPreApproval),
      ...settingsWireValues(state.settings),
    }

    try {
      await saveProfile(orgId, agreementCode, okVersion, body, etag)
      onSaved()
    } catch (err) {
      const e = err as Error & {
        status?: number
        body?: {
          error?: string
          message?: string
          fields?: ProfileFieldError[]
          expectedVersion?: number
          actualVersion?: number
        }
      }
      if (e.status === 412) {
        // Stale state: another admin saved while this user was editing.
        // S22 / ADR-018 D7: body carries expectedVersion + actualVersion.
        setStaleConflict({
          expected: e.body?.expectedVersion,
          actual: e.body?.actualVersion,
        })
      } else if (e.status === 400 && e.body?.fields && e.body.fields.length > 0) {
        // Structured per-field errors from ConfigEndpoints (ADR-017 D9a):
        //   { error, fields: [{ field, code, nearestValid? }, ...] }
        // Render each under its own input.
        // The first error for a field wins; an entry naming a field the editor does not render
        // (or no field) goes to the banner, alone or mixed with placeable ones.
        const next: Record<string, ProfileFieldError> = {}
        let unplaceable = false
        for (const f of e.body.fields) {
          if (f.field && KNOWN_ERROR_FIELDS.has(f.field)) {
            if (!(f.field in next)) next[f.field] = f
          } else {
            unplaceable = true
          }
        }
        setFieldErrors(next)
        if (unplaceable) setSaveError(UNPLACEABLE_REFUSAL)
      } else if (e.status === 400 && e.body?.error === 'Invalid profile supersession') {
        // S22 / ADR-018 D7: backdate-before-predecessor rejection
        // (InvalidProfileSupersessionException). No `fields` payload — surface
        // the explanatory message in the existing error banner.
        setSaveError(e.body.message || 'Ugyldig profil-supersession.')
      } else {
        setSaveError(e.body?.error || e.message || 'Kunne ikke gemme profil.')
      }
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return <div style={{ padding: '1rem' }}>Indlaeser profil...</div>
  }
  if (loadError) {
    return <Alert variant="error">{loadError}</Alert>
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      <Card>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '1rem' }}>
          <div>
            <div style={{ fontWeight: 600 }}>{orgLabel}</div>
            <div style={{ fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
              {agreementCode} / {okVersion}
            </div>
            <div style={{ fontSize: '0.8125rem', color: 'var(--color-text-secondary)', marginTop: '0.25rem' }}>
              {profile
                ? `Profil-ID: ${profile.profileId}`
                : 'Ingen lokal profil — central konfiguration gaelder. Gem for at oprette.'}
            </div>
          </div>
          {/*
            Deactivation endpoint deferred — TASK-2107 did not add a DELETE.
            We render the button so the affordance is visible, but disable it
            with an explanatory `title` tooltip. DO NOT call any endpoint.
          */}
          <Button
            variant="secondary"
            disabled
            title="Deaktivering vil blive tilfoejet i en senere opgave"
          >
            Deaktiver profil
          </Button>
        </div>
      </Card>

      {staleConflict && (
        <Alert variant="warning">
          Din redigering var baseret paa en forældet tilstand. Profilen er blevet opdateret
          siden. Genindlaes og gennemgaa aendringerne foer du gemmer igen.
          {staleConflict.expected !== undefined && staleConflict.actual !== undefined && (
            <> {' '}
              (Forventet version {staleConflict.expected}, aktuel version {staleConflict.actual}.)
            </>
          )}
        </Alert>
      )}
      {saveError && <Alert variant="error">{saveError}</Alert>}

      <Card header={<div style={{ fontWeight: 600 }}>Lokale tilpasninger</div>}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
          <FormField
            label="Gaelder fra"
            htmlFor="profile-effective-from"
            required
            error={fieldErrorMessage(fieldErrors['EffectiveFrom'])}
          >
            <MondayDatePicker
              id="profile-effective-from"
              value={state.effectiveFrom}
              onChange={(next) => setState(prev => ({ ...prev, effectiveFrom: next }))}
              mondayOnly={weeklyNormHoursIsChanged}
              pastOrTodayOnly={true}
              disabled={saving}
            />
          </FormField>

          {OVERRIDABLE_NUMBER_FIELDS.map(spec => {
            const draft = state.numbers[spec.key]
            const central = readCentralNumber(centralConfig, spec.key)
            const fieldError = fieldErrors[spec.backendName]
            return (
              <NumberOverrideRow
                key={spec.key}
                id={`override-${spec.key}`}
                label={spec.label}
                note={spec.note}
                draft={draft}
                centralValue={central}
                fieldError={fieldError}
                disabled={saving}
                onToggle={(enabled) => updateNumber(spec.key, { enabled, text: enabled ? draft.text : '' })}
                onTextChange={(text) => updateNumber(spec.key, { text })}
              />
            )
          })}

          {OVERRIDABLE_BOOL_FIELDS.map(spec => {
            const draft = state.bools[spec.key]
            const central = readCentralBool(centralConfig, spec.key)
            const fieldError = fieldErrors[spec.backendName]
            return (
              <BoolOverrideRow
                key={spec.key}
                id={`override-${spec.key}`}
                label={spec.label}
                note={spec.note}
                draft={draft}
                centralValue={central}
                fieldError={fieldError}
                disabled={saving}
                onToggle={(enabled) => updateBool(spec.key, { enabled })}
                onValueChange={(value) => updateBool(spec.key, { value })}
              />
            )
          })}
        </div>
      </Card>

      <Card header={<div style={{ fontWeight: 600 }}>{CARD_HEADING}</div>}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
          {FIELDS_BEFORE_OFFICE_HOURS.map(member => isChoiceMember(member) ? (
            <ChoiceGroup
              key={member}
              member={member}
              value={state.settings[member]}
              error={fieldErrors[pascalName(member)]}
              disabled={saving}
              onChange={(v) => updateSetting(member, v)}
            />
          ) : (
            <CapInput
              key={member}
              member={member}
              value={state.settings[member]}
              error={fieldErrors[pascalName(member)]}
              disabled={saving}
              onChange={(v) => updateSetting(member, v)}
            />
          ))}

          <OfficeHoursGroup
            settings={state.settings}
            warning={officeWarning}
            fieldErrors={fieldErrors}
            disabled={saving}
            onChange={updateSetting}
          />

          <ChoiceGroup
            member="paidAbsenceCreditBasis"
            value={state.settings.paidAbsenceCreditBasis}
            error={fieldErrors[pascalName('paidAbsenceCreditBasis')]}
            disabled={saving}
            onChange={(v) => updateSetting('paidAbsenceCreditBasis', v)}
            analogyLabel={agreementCode === 'AC' || agreementCode.startsWith('AC_')}
            officeHoursUnavailable={!hasWindow}
          />
        </div>
      </Card>

      <Card header={<div style={{ fontWeight: 600 }}>Centrale vaerdier (kan ikke overskrives)</div>}>
        {centralConfig ? (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.5rem 1rem', fontSize: '0.875rem' }}>
            {READ_ONLY_FIELDS.map(spec => (
              <div key={spec.key} style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid var(--color-border)', padding: '0.25rem 0' }}>
                <span style={{ color: 'var(--color-text-secondary)' }}>{spec.label}</span>
                <span>{formatCentralValue(centralConfig[spec.key])}</span>
              </div>
            ))}
          </div>
        ) : (
          <div style={{ fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
            Ingen aktiv central konfiguration fundet for {agreementCode}/{okVersion}.
          </div>
        )}
      </Card>

      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.5rem' }}>
        <Button
          variant="primary"
          onClick={handleSave}
          disabled={!hasAnyChange || saving}
        >
          {saving ? 'Gemmer...' : (profile ? 'Gem aendringer' : 'Opret profil')}
        </Button>
      </div>
    </div>
  )
}

// ── Sub-components. ──

interface NumberOverrideRowProps {
  id: string
  label: string
  draft: NumberDraft
  centralValue: number | null
  fieldError: ProfileFieldError | undefined
  disabled: boolean
  /** A static per-field note, associated with the field's controls (aria-describedby). */
  note?: string
  onToggle: (enabled: boolean) => void
  onTextChange: (text: string) => void
}

function NumberOverrideRow({
  id, label, draft, centralValue, fieldError, disabled, note, onToggle, onTextChange,
}: NumberOverrideRowProps) {
  const noteId = note ? `${id}-note` : undefined
  return (
    <FormField label={label} htmlFor={id} error={fieldErrorMessage(fieldError)}>
      <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
        <Checkbox
          id={`${id}-toggle`}
          label="Overskriv central"
          checked={draft.enabled}
          onChange={(e) => onToggle(e.target.checked)}
          disabled={disabled}
          describedBy={noteId}
        />
        {draft.enabled ? (
          <Input
            id={id}
            type="number"
            step="any"
            value={draft.text}
            onChange={(e) => onTextChange(e.target.value)}
            disabled={disabled}
            aria-describedby={noteId}
            error={Boolean(fieldError)}
          />
        ) : (
          <span style={{ fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
            Central: {centralValue ?? '—'}
          </span>
        )}
      </div>
      {note && (
        <p id={noteId} style={{ margin: '0.25rem 0 0', fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
          {note}
        </p>
      )}
    </FormField>
  )
}

interface BoolOverrideRowProps {
  id: string
  label: string
  draft: BoolDraft
  centralValue: boolean | null
  fieldError: ProfileFieldError | undefined
  disabled: boolean
  /** A static per-field note, associated with the field's controls (aria-describedby). */
  note?: string
  onToggle: (enabled: boolean) => void
  onValueChange: (value: boolean) => void
}

function BoolOverrideRow({
  id, label, draft, centralValue, fieldError, disabled, note, onToggle, onValueChange,
}: BoolOverrideRowProps) {
  const noteId = note ? `${id}-note` : undefined
  return (
    <FormField label={label} htmlFor={id} error={fieldErrorMessage(fieldError)}>
      <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
        <Checkbox
          id={`${id}-toggle`}
          label="Overskriv central"
          checked={draft.enabled}
          onChange={(e) => onToggle(e.target.checked)}
          disabled={disabled}
          describedBy={noteId}
        />
        {draft.enabled ? (
          <Checkbox
            id={id}
            label={draft.value ? 'Ja' : 'Nej'}
            checked={draft.value}
            onChange={(e) => onValueChange(e.target.checked)}
            disabled={disabled}
            describedBy={noteId}
          />
        ) : (
          <span style={{ fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
            Central: {centralValue === null ? '—' : centralValue ? 'Ja' : 'Nej'}
          </span>
        )}
      </div>
      {note && (
        <p id={noteId} style={{ margin: '0.25rem 0 0', fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
          {note}
        </p>
      )}
    </FormField>
  )
}

// ── The institution's own settings (profileSettingsCopy.ts holds every string). ──

const noteStyle = { margin: '0.25rem 0 0', fontSize: '0.875rem', color: 'var(--color-text-secondary)' }
const errorStyle = { color: 'var(--color-error)', fontSize: '14px', lineHeight: 1.5 }
const fieldsetStyle = { border: 'none', padding: 0, margin: 0, minWidth: 0 }
const legendStyle = { fontWeight: 500, padding: 0, marginBottom: '0.25rem' }

function joinIds(...ids: (string | false | undefined)[]): string | undefined {
  const out = ids.filter(Boolean).join(' ')
  return out === '' ? undefined : out
}

// FormField's own error span has no id, so an input cannot point at it; the new fields render
// their message here instead.
function SettingError({ id, message }: { id: string; message: string | undefined }) {
  return message ? <span id={id} role="alert" style={errorStyle}>{message}</span> : null
}

interface ChoiceGroupProps {
  member: ChoiceMember
  value: string
  error: ProfileFieldError | undefined
  disabled: boolean
  onChange: (value: string) => void
  /** AC family only: the "default by analogy" label directly under the legend. */
  analogyLabel?: boolean
  /** Credit basis only: no weekday has both times, so "Kontortid" cannot be saved. */
  officeHoursUnavailable?: boolean
}

function ChoiceGroup({
  member, value, error, disabled, onChange, analogyLabel = false, officeHoursUnavailable = false,
}: ChoiceGroupProps) {
  const spec = CHOICE_SPECS[member]
  const base = `profile-setting-${member}`
  const message = fieldErrorMessage(error)
  // No aria-invalid on a fieldset: a group's refusal is announced through its description.
  return (
    <fieldset
      id={base}
      style={fieldsetStyle}
      aria-describedby={joinIds(
        `${base}-note`, message && `${base}-error`, analogyLabel && `${base}-analogy`,
        officeHoursUnavailable && `${base}-hint`,
      )}
    >
      <legend style={legendStyle}>{spec.legend}</legend>
      {analogyLabel && <p id={`${base}-analogy`} style={noteStyle}>{D8B_LABEL}</p>}
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem' }}>
        {[{ value: '', label: spec.notSet }, ...spec.options].map(opt => (
          <Radio
            key={opt.value || 'none'}
            id={`${base}-${opt.value || 'none'}`}
            name={base}
            value={opt.value}
            label={opt.label}
            checked={value === opt.value}
            onChange={() => onChange(opt.value)}
            disabled={disabled || (officeHoursUnavailable && opt.value === 'OFFICE_HOURS' && value !== 'OFFICE_HOURS')}
          />
        ))}
      </div>
      {officeHoursUnavailable && <p id={`${base}-hint`} style={noteStyle}>{KONTORTID_HINT}</p>}
      <p id={`${base}-note`} style={noteStyle}>{D6_NOTE}</p>
      <SettingError id={`${base}-error`} message={message} />
    </fieldset>
  )
}

interface CapInputProps {
  member: CapMember
  value: string
  error: ProfileFieldError | undefined
  disabled: boolean
  onChange: (value: string) => void
}

function CapInput({ member, value, error, disabled, onChange }: CapInputProps) {
  const base = `profile-setting-${member}`
  const message = fieldErrorMessage(error)
  return (
    <FormField label={CAP_LABELS[member]} htmlFor={base}>
      <Input
        id={base}
        type="number"
        min="0"
        step="any"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        aria-describedby={joinIds(`${base}-note`, message && `${base}-error`)}
        error={Boolean(message)}
      />
      <p id={`${base}-note`} style={noteStyle}>{D6_NOTE}</p>
      <SettingError id={`${base}-error`} message={message} />
    </FormField>
  )
}

interface OfficeHoursGroupProps {
  settings: Record<SettingMember, string>
  warning: string
  fieldErrors: Record<string, ProfileFieldError>
  disabled: boolean
  onChange: (member: SettingMember, value: string) => void
}

function OfficeHoursGroup({ settings, warning, fieldErrors, disabled, onChange }: OfficeHoursGroupProps) {
  const base = 'profile-setting-officeHours'
  const time = (member: OfficeHoursMember, label: string) => {
    const id = `profile-setting-${member}`
    const message = fieldErrorMessage(fieldErrors[pascalName(member)])
    return (
      <div key={member} style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem' }}>
        <Label htmlFor={id}>{label}</Label>
        <Input
          id={id}
          type="time"
          value={settings[member]}
          onChange={(e) => onChange(member, e.target.value)}
          disabled={disabled}
          aria-describedby={joinIds(`${base}-note`, message && `${id}-error`)}
          error={Boolean(message)}
        />
        <SettingError id={`${id}-error`} message={message} />
      </div>
    )
  }
  return (
    <fieldset id={base} style={fieldsetStyle} aria-describedby={joinIds(`${base}-note`, warning !== '' && `${base}-warning`)}>
      <legend style={legendStyle}>{OFFICE_HOURS_LEGEND}</legend>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
        {WEEKDAYS.map(d => (
          <div key={d.key} style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
            {time(`officeHours${d.key}Start`, `${d.name} fra`)}
            {time(`officeHours${d.key}End`, `${d.name} til`)}
          </div>
        ))}
      </div>
      <p id={`${base}-note`} style={noteStyle}>{D6_NOTE}</p>
      {/* Always mounted so a screen reader announces the change; a warning, never a refusal. */}
      <p id={`${base}-warning`} aria-live="polite" style={noteStyle}>{warning}</p>
    </fieldset>
  )
}

// ── Helpers. ──

function pascalName(member: string): string {
  return member.charAt(0).toUpperCase() + member.slice(1)
}

function clockMinutes(text: string): number | null {
  const m = /^(\d{2}):(\d{2})/.exec(text)
  return m ? Number(m[1]) * 60 + Number(m[2]) : null
}

/** Whole minutes over the weekdays with a complete window (start before end); null with none. */
function officeHoursMinutes(settings: Record<SettingMember, string>): number | null {
  let sum = 0
  let any = false
  for (const d of WEEKDAYS) {
    const from = clockMinutes(settings[`officeHours${d.key}Start`])
    const to = clockMinutes(settings[`officeHours${d.key}End`])
    if (from !== null && to !== null && from < to) {
      sum += to - from
      any = true
    }
  }
  return any ? sum : null
}

function settingWire(member: SettingMember, text: string): number | string | null {
  const t = text.trim()
  if (t === '') return null
  if (isChoiceMember(member)) return t
  if (CAP_MEMBERS.includes(member as CapMember)) {
    const n = Number(t)
    return Number.isFinite(n) ? n : null
  }
  // The server binds "HH:mm:ss"; "HH:mm" does not bind.
  return /^\d{2}:\d{2}$/.test(t) ? `${t}:00` : t
}

function settingDraft(profile: LocalAgreementProfile | null, member: SettingMember): string {
  const v = profile?.[member]
  if (v === null || v === undefined) return ''
  return typeof v === 'string' && !isChoiceMember(member) ? v.slice(0, 5) : String(v)
}

function settingsWireValues(settings: Record<SettingMember, string>): Pick<ProfileSaveRequest, SettingMember> {
  const out: Record<string, number | string | null> = {}
  for (const m of ALL_SETTING_MEMBERS) out[m] = settingWire(m, settings[m])
  return out as Pick<ProfileSaveRequest, SettingMember>
}

function buildInitialState(profile: LocalAgreementProfile | null): EditorState {
  return {
    effectiveFrom: profile?.effectiveFrom ?? '',
    numbers: {
      weeklyNormHours:           draftFromNumber(profile?.weeklyNormHours),
      maxFlexBalance:            draftFromNumber(profile?.maxFlexBalance),
      maxOvertimeHoursPerPeriod: draftFromNumber(profile?.maxOvertimeHoursPerPeriod),
    },
    bools: {
      overtimeRequiresPreApproval: draftFromBool(profile?.overtimeRequiresPreApproval ?? null),
    },
    settings: Object.fromEntries(
      ALL_SETTING_MEMBERS.map(m => [m, settingDraft(profile, m)]),
    ) as Record<SettingMember, string>,
  }
}

function draftFromNumber(value: number | null | undefined): NumberDraft {
  if (value === null || value === undefined) {
    return { enabled: false, text: '' }
  }
  return { enabled: true, text: String(value) }
}

function draftFromBool(value: boolean | null): BoolDraft {
  if (value === null) {
    return { enabled: false, value: false }
  }
  return { enabled: true, value }
}

function pickNumber(draft: NumberDraft): number | null {
  if (!draft.enabled) return null
  const trimmed = draft.text.trim()
  if (trimmed === '') return null
  const parsed = Number(trimmed)
  if (!Number.isFinite(parsed)) return null
  return parsed
}

function pickBool(draft: BoolDraft): boolean | null {
  if (!draft.enabled) return null
  return draft.value
}

function readCentralNumber(cfg: AgreementConfig | null, key: OverridableNumberField): number | null {
  if (!cfg) return null
  // Of the overridable number fields, weeklyNormHours and maxFlexBalance exist on AgreementConfig
  // with the same names.
  // maxOvertimeHoursPerPeriod is NOT on the AgreementConfig interface — it's
  // overridable-only — so we return null for it (no central to display).
  switch (key) {
    case 'weeklyNormHours':           return cfg.weeklyNormHours
    case 'maxFlexBalance':            return cfg.maxFlexBalance
    case 'maxOvertimeHoursPerPeriod': return null
  }
}

function readCentralBool(cfg: AgreementConfig | null, _key: OverridableBoolField): boolean | null {
  if (!cfg) return null
  // overtimeRequiresPreApproval is overridable-only — not present on the
  // central AgreementConfig interface. Return null (no central to display).
  return null
}

function formatCentralValue(value: unknown): string {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'boolean') return value ? 'Ja' : 'Nej'
  return String(value)
}

function fieldErrorMessage(err: ProfileFieldError | undefined): string | undefined {
  if (!err) return undefined
  // ADR-017 D9a structured codes — surface user-readable Danish text per code.
  // Per task spec, NOT_MONDAY_ALIGNED gets "Use a Monday: [date1] or [date2]";
  // EFFECTIVE_FROM_NOT_TODAY_OR_PAST gets the today-or-past message.
  if (err.code === 'NOT_MONDAY_ALIGNED') {
    const dates = (err.nearestValid ?? []).join(' eller ')
    return dates
      ? `Vaelg en mandag: ${dates}`
      : 'Datoen skal vaere en mandag.'
  }
  if (err.code === 'EFFECTIVE_FROM_NOT_TODAY_OR_PAST') {
    return 'Gaelder-fra skal vaere i dag eller tidligere — den nuvaerende tilstand.'
  }
  if (err.code && CODE_MESSAGES[err.code]) return CODE_MESSAGES[err.code]
  if (err.message) return err.message
  if (err.code) return UNPLACEABLE_REFUSAL
  return 'Validering fejlede.'
}

function computeChangedFieldNames(
  profile: LocalAgreementProfile | null,
  state: EditorState,
): string[] {
  const out: string[] = []
  for (const spec of OVERRIDABLE_NUMBER_FIELDS) {
    const next = pickNumber(state.numbers[spec.key])
    const prev = profile ? (profile[spec.key] ?? null) : null
    if (!nullableNumberEqual(prev, next)) out.push(spec.backendName)
  }
  for (const spec of OVERRIDABLE_BOOL_FIELDS) {
    const next = pickBool(state.bools[spec.key])
    const prev = profile ? (profile[spec.key] ?? null) : null
    if (!nullableBoolEqual(prev, next)) out.push(spec.backendName)
  }
  for (const m of ALL_SETTING_MEMBERS) {
    if (settingWire(m, state.settings[m]) !== settingWire(m, settingDraft(profile, m))) out.push(pascalName(m))
  }
  return out
}

function nullableNumberEqual(a: number | null, b: number | null): boolean {
  if (a === null && b === null) return true
  if (a === null || b === null) return false
  return a === b
}

function nullableBoolEqual(a: boolean | null, b: boolean | null): boolean {
  if (a === null && b === null) return true
  if (a === null || b === null) return false
  return a === b
}
