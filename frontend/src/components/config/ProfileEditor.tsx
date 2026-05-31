// Local agreement profile editor (S21 / ADR-017 / TASK-2109).
//
// One editor instance per (org, agreement, OkVersion). Renders:
//   - A header with profile metadata + a disabled "deactivate" button
//     (no DELETE endpoint yet — see deferred-items note below).
//   - Five overridable fields with an "Override central" toggle. When the
//     toggle is OFF the central value is shown read-only and the saved
//     value is NULL ("inherit central"). When ON the user types a value
//     and that value is what gets PUT.
//   - The remaining ~21 central agreement-config fields rendered read-only
//     ("see what you can't override"). These come from the central agreement
//     config and are NOT part of the profile schema.
//   - An effective-from date picker. mondayOnly switches dynamically based on
//     whether WeeklyNormHours is in the changed-fields set; pastOrTodayOnly is
//     always true.
//   - A Save button that's disabled until at least one field has changed
//     vs the loaded profile (or vs central if no profile exists). On click,
//     the editor PUTs only what's different.
//   - 412 stale-state banner + 400 per-field error rendering.
//
// SCOPE: basic functional only. No visual polish — Phase 5 owns that. The
// component intentionally uses `useState` only; no global state, no form
// library.
import { useEffect, useMemo, useState } from 'react'
import { Button, Card, Alert, FormField, Input, Checkbox } from '../ui'
import { saveProfile, type LocalAgreementProfile, type ProfileFieldError } from '../../hooks/useConfig'
import type { AgreementConfig } from '../../hooks/useAgreementConfigs'
import { MondayDatePicker } from './MondayDatePicker'

// ── Field metadata. ──
//
// Five overridable fields — must mirror the backend's ProfileSaveRequest /
// LocalAgreementProfile. Adding a sixth requires schema + alignment-policy
// changes upstream; this list is the UI side of that contract.
type OverridableNumberField = 'weeklyNormHours' | 'maxFlexBalance' | 'flexCarryoverMax' | 'maxOvertimeHoursPerPeriod'
type OverridableBoolField = 'overtimeRequiresPreApproval'

const OVERRIDABLE_NUMBER_FIELDS: { key: OverridableNumberField; label: string; backendName: string }[] = [
  { key: 'weeklyNormHours',           label: 'Ugentlig normtimer',         backendName: 'WeeklyNormHours' },
  { key: 'maxFlexBalance',            label: 'Maks. flexsaldo',            backendName: 'MaxFlexBalance' },
  { key: 'flexCarryoverMax',          label: 'Maks. flexoverfoersel',      backendName: 'FlexCarryoverMax' },
  { key: 'maxOvertimeHoursPerPeriod', label: 'Maks. overarbejde pr periode', backendName: 'MaxOvertimeHoursPerPeriod' },
]

const OVERRIDABLE_BOOL_FIELDS: { key: OverridableBoolField; label: string; backendName: string }[] = [
  { key: 'overtimeRequiresPreApproval', label: 'Overarbejde kraever forhaandsgodkendelse', backendName: 'OvertimeRequiresPreApproval' },
]

// Read-only central fields — every AgreementConfig field that is NOT one of the
// 5 overridable ones, minus the bookkeeping fields (configId, status, dates,
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
}

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
      flexCarryoverMax: pickNumber(state.numbers.flexCarryoverMax),
      maxOvertimeHoursPerPeriod: pickNumber(state.numbers.maxOvertimeHoursPerPeriod),
      overtimeRequiresPreApproval: pickBool(state.bools.overtimeRequiresPreApproval),
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
        const next: Record<string, ProfileFieldError> = {}
        for (const f of e.body.fields) {
          if (f.field) next[f.field] = f
        }
        if (Object.keys(next).length > 0) {
          setFieldErrors(next)
        } else {
          setSaveError(e.body.error || e.message || 'Validering fejlede.')
        }
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
  onToggle: (enabled: boolean) => void
  onTextChange: (text: string) => void
}

function NumberOverrideRow({
  id, label, draft, centralValue, fieldError, disabled, onToggle, onTextChange,
}: NumberOverrideRowProps) {
  return (
    <FormField label={label} htmlFor={id} error={fieldErrorMessage(fieldError)}>
      <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
        <Checkbox
          id={`${id}-toggle`}
          label="Overskriv central"
          checked={draft.enabled}
          onChange={(e) => onToggle(e.target.checked)}
          disabled={disabled}
        />
        {draft.enabled ? (
          <Input
            id={id}
            type="number"
            step="any"
            value={draft.text}
            onChange={(e) => onTextChange(e.target.value)}
            disabled={disabled}
            error={Boolean(fieldError)}
          />
        ) : (
          <span style={{ fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
            Central: {centralValue ?? '—'}
          </span>
        )}
      </div>
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
  onToggle: (enabled: boolean) => void
  onValueChange: (value: boolean) => void
}

function BoolOverrideRow({
  id, label, draft, centralValue, fieldError, disabled, onToggle, onValueChange,
}: BoolOverrideRowProps) {
  return (
    <FormField label={label} htmlFor={id} error={fieldErrorMessage(fieldError)}>
      <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
        <Checkbox
          id={`${id}-toggle`}
          label="Overskriv central"
          checked={draft.enabled}
          onChange={(e) => onToggle(e.target.checked)}
          disabled={disabled}
        />
        {draft.enabled ? (
          <Checkbox
            id={id}
            label={draft.value ? 'Ja' : 'Nej'}
            checked={draft.value}
            onChange={(e) => onValueChange(e.target.checked)}
            disabled={disabled}
          />
        ) : (
          <span style={{ fontSize: '0.875rem', color: 'var(--color-text-secondary)' }}>
            Central: {centralValue === null ? '—' : centralValue ? 'Ja' : 'Nej'}
          </span>
        )}
      </div>
    </FormField>
  )
}

// ── Helpers. ──

function buildInitialState(profile: LocalAgreementProfile | null): EditorState {
  return {
    effectiveFrom: profile?.effectiveFrom ?? '',
    numbers: {
      weeklyNormHours:           draftFromNumber(profile?.weeklyNormHours),
      maxFlexBalance:            draftFromNumber(profile?.maxFlexBalance),
      flexCarryoverMax:          draftFromNumber(profile?.flexCarryoverMax),
      maxOvertimeHoursPerPeriod: draftFromNumber(profile?.maxOvertimeHoursPerPeriod),
    },
    bools: {
      overtimeRequiresPreApproval: draftFromBool(profile?.overtimeRequiresPreApproval ?? null),
    },
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
  // The 4 overridable number fields exist on AgreementConfig with the same names
  // (weeklyNormHours, maxFlexBalance, flexCarryoverMax, maxOvertimeHoursPerPeriod).
  // maxOvertimeHoursPerPeriod is NOT on the AgreementConfig interface — it's
  // overridable-only — so we return null for it (no central to display).
  switch (key) {
    case 'weeklyNormHours':           return cfg.weeklyNormHours
    case 'maxFlexBalance':            return cfg.maxFlexBalance
    case 'flexCarryoverMax':          return cfg.flexCarryoverMax
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
  if (err.message) return err.message
  if (err.code) return err.code
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
