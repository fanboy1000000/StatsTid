import { useState, type ChangeEvent, type FormEvent } from 'react'
import {
  useEntitlementConfigList,
  useEntitlementConfigActions,
  isEntitlementConfigMutationError,
  type EntitlementConfig,
  type EntitlementConfigPatch,
  type EntitlementConfigCreateRequest,
  type WithEtag,
} from '../../hooks/useEntitlementConfig'
import type { EntitlementType, AccrualModel } from '../../lib/entitlementConstants'
import {
  TYPE_LABELS,
  TYPE_OPTIONS,
  ACCRUAL_OPTIONS,
  MONTH_LABELS,
  entitlementTypeLabel,
  accrualModelLabel,
} from '../../lib/entitlementConstants'
import { Spinner } from '../../components/ui'
import styles from './EntitlementConfigEditor.module.css'

// TASK-3009 / Phase 4d-2 / ADR-021 pending: admin CRUD UI for entitlement
// configs. Mirrors S25 admin pages (WageTypeMappingManagement /
// PositionOverrideManagement) for the table + 412 banner-with-retry shape;
// mirrors S29 ProfileEditor for the same-day-only-edit semantics by NOT
// exposing an effective_from picker (per Q4 + ADR-021 §3, the server stamps
// effective_from = today and same-day edits in-place vs cross-day supersede).
//
// S118 / TASK-11801 (PAT-012) — the row/request types are the GENERATED spec
// types (via the hook aliases): the wire `entitlementType` / `accrualModel`
// are OPEN strings, so read-side label lookups go through the runtime-guarded
// helpers (`entitlementTypeLabel` / `accrualModelLabel`); the CREATE form
// keeps the narrow UI types (its selects only offer the known sets). No `as`
// casts remain (this file is on the no-`as` lint surface).

interface EditFormState {
  annualQuota: string
  carryoverMax: string
  description: string
  proRateByPartTime: boolean
  isPerEpisode: boolean
  minAge: string
}

interface CreateFormState extends EditFormState {
  entitlementType: EntitlementType
  agreementCode: string
  okVersion: string
  accrualModel: AccrualModel
  resetMonth: string
  // S73 / TASK-7301 — the full-day-only flag on create (server 422s a false for
  // CARE_DAY/SENIOR_DAY via the construction-enforcement guard).
  fullDayOnly: boolean
}

const emptyEditForm: EditFormState = {
  annualQuota: '0',
  carryoverMax: '0',
  description: '',
  proRateByPartTime: false,
  isPerEpisode: false,
  minAge: '',
}

const emptyCreateForm: CreateFormState = {
  ...emptyEditForm,
  entitlementType: 'VACATION',
  agreementCode: '',
  okVersion: '',
  accrualModel: 'IMMEDIATE',
  resetMonth: '1',
  fullDayOnly: false,
}

function parseNumericField(value: string, fallback: number): number {
  const parsed = Number.parseFloat(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parseOptionalInt(value: string): number | null {
  if (!value.trim()) return null
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : null
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('da-DK')
  } catch {
    return iso
  }
}

function configToEditForm(c: EntitlementConfig): EditFormState {
  return {
    annualQuota: String(c.annualQuota),
    carryoverMax: String(c.carryoverMax),
    description: c.description ?? '',
    proRateByPartTime: c.proRateByPartTime,
    isPerEpisode: c.isPerEpisode,
    minAge: c.minAge === null ? '' : String(c.minAge),
  }
}

function editFormToPatch(f: EditFormState): EntitlementConfigPatch {
  return {
    annualQuota: parseNumericField(f.annualQuota, 0),
    carryoverMax: parseNumericField(f.carryoverMax, 0),
    description: f.description.trim() ? f.description.trim() : null,
    proRateByPartTime: f.proRateByPartTime,
    isPerEpisode: f.isPerEpisode,
    minAge: parseOptionalInt(f.minAge),
  }
}

function createFormToRequest(f: CreateFormState): EntitlementConfigCreateRequest {
  return {
    ...editFormToPatch(f),
    entitlementType: f.entitlementType,
    agreementCode: f.agreementCode.trim(),
    okVersion: f.okVersion.trim(),
    accrualModel: f.accrualModel,
    resetMonth: Number.parseInt(f.resetMonth, 10) || 1,
    fullDayOnly: f.fullDayOnly,
  }
}

export function EntitlementConfigEditor() {
  const { configs, loading, error, refetch } = useEntitlementConfigList()
  const { createConfig, updateConfig, deleteConfig } = useEntitlementConfigActions()

  const [showCreate, setShowCreate] = useState(false)
  const [createForm, setCreateForm] = useState<CreateFormState>(emptyCreateForm)
  const [editing, setEditing] = useState<WithEtag<EntitlementConfig> | null>(null)
  const [editForm, setEditForm] = useState<EditFormState>(emptyEditForm)

  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  // S25/S29 banner-with-retry precedent (ProfileEditor.tsx:135/213-220/283-293).
  // 412 from update/delete sets `staleConflict`; the Genindlaes button refetches
  // the list and clears the banner.
  const [staleConflict, setStaleConflict] = useState<{
    expected?: number
    actual?: number
  } | null>(null)

  // S118: runtime type guard instead of an `as` cast (PAT-012 no-`as` surface).
  const handleMutationError = (err: unknown) => {
    if (isEntitlementConfigMutationError(err) && err.status === 412) {
      setStaleConflict({
        expected: err.body?.expectedVersion,
        actual: err.body?.actualVersion,
      })
    } else if (isEntitlementConfigMutationError(err) && err.status === 422 && err.body?.immutable) {
      setFormError(
        `Felterne ${err.body.immutable.join(', ')} er fastlaast og kan ikke aendres via dette administratorbillede. Opret en ny OK-version i stedet.`,
      )
    } else {
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleStaleRefresh = async () => {
    setStaleConflict(null)
    setEditing(null)
    await refetch()
  }

  const openCreate = () => {
    setCreateForm(emptyCreateForm)
    setFormError(null)
    setShowCreate(true)
  }

  const closeCreate = () => {
    setShowCreate(false)
    setCreateForm(emptyCreateForm)
    setFormError(null)
  }

  const openEdit = (config: WithEtag<EntitlementConfig>) => {
    setEditing(config)
    setEditForm(configToEditForm(config))
    setFormError(null)
  }

  const closeEdit = () => {
    setEditing(null)
    setEditForm(emptyEditForm)
    setFormError(null)
  }

  const handleCreateSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!createForm.agreementCode.trim() || !createForm.okVersion.trim()) {
      setFormError('Udfyld venligst overenskomstkode og OK-version.')
      return
    }
    setSubmitting(true)
    setFormError(null)
    try {
      await createConfig(createFormToRequest(createForm))
      closeCreate()
      await refetch()
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  const handleEditSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!editing) return
    setSubmitting(true)
    setFormError(null)
    try {
      // Backend UpdateEntitlementConfigRequest requires the full shape:
      // natural-key + frozen fields (immutable per Q1 sub-fork (i)) + editable
      // patch + explicit effectiveFrom=today (cycle-3 same-day-only-edit
      // validator). Source the immutable fields from `editing` directly — the
      // page displays them read-only, so the round-trip matches the
      // predecessor row and the server's freeze guard is a no-op on the
      // happy path.
      const today = new Date().toISOString().slice(0, 10)
      await updateConfig(editing.configId, editing.etag, {
        ...editFormToPatch(editForm),
        entitlementType: editing.entitlementType,
        agreementCode: editing.agreementCode,
        okVersion: editing.okVersion,
        accrualModel: editing.accrualModel,
        resetMonth: editing.resetMonth,
        effectiveFrom: today,
        // S73 R2 version-survival — carry the predecessor's flag round-trip so an
        // unrelated field edit never resets it (the page displays it read-only).
        fullDayOnly: editing.fullDayOnly,
      })
      closeEdit()
      await refetch()
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  const handleDelete = async (config: WithEtag<EntitlementConfig>) => {
    const label = `${entitlementTypeLabel(config.entitlementType)} (${config.agreementCode} ${config.okVersion})`
    const ok = window.confirm(
      `Slet konfigurationen for ${label}?\n\nDette markerer raekken som slettet (soft-delete). Administratorer kan oprette en ny effective_from-raekke senere.`,
    )
    if (!ok) return
    setSubmitting(true)
    setFormError(null)
    try {
      await deleteConfig(config.configId, config.etag)
      await refetch()
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  // S118 — `instanceof` narrowing instead of the previous `as` casts (no-`as`
  // surface): a checkbox is always an HTMLInputElement; selects/textareas take
  // the `.value` branch. The computed-key upsert keeps the existing (weakly
  // typed) form-state pattern unchanged.
  const setCreateField =
    (field: keyof CreateFormState) =>
    (e: ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
      const target = e.target
      const value =
        target instanceof HTMLInputElement && target.type === 'checkbox'
          ? target.checked
          : target.value
      setCreateForm((f) => ({ ...f, [field]: value }))
    }

  const setEditField =
    (field: keyof EditFormState) =>
    (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      const target = e.target
      const value =
        target instanceof HTMLInputElement && target.type === 'checkbox'
          ? target.checked
          : target.value
      setEditForm((f) => ({ ...f, [field]: value }))
    }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Berettigelser</h1>
        <button className={styles.createBtn} onClick={openCreate}>
          Opret ny
        </button>
      </div>

      {staleConflict && (
        <div className={styles.alert} role="alert" data-testid="stale-conflict-banner">
          Din handling var baseret paa en foraeldet tilstand. En anden administrator har aendret konfigurationen siden du indlaeste siden.
          {staleConflict.expected !== undefined && staleConflict.actual !== undefined && (
            <> {' '}(Forventet version {staleConflict.expected}, aktuel version {staleConflict.actual}.)</>
          )}
          {' '}
          <button type="button" className={styles.actionBtn} onClick={handleStaleRefresh}>
            Genindlaes
          </button>
        </div>
      )}
      {error && <div className={styles.alert}>{error}</div>}

      {loading && <div className={styles.spinner}><Spinner size="lg" /></div>}

      {!loading && !error && configs.length === 0 && (
        <div className={styles.emptyState}>Ingen berettigelser fundet</div>
      )}

      {!loading && configs.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Type</th>
              <th>Aftale</th>
              <th>OK-version</th>
              <th className={styles.numeric}>Aarlig kvote</th>
              <th className={styles.numeric}>Overfoersel</th>
              <th>Nulstilling</th>
              <th>Gaeldende fra</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {configs.map((config) => (
              <tr key={config.configId}>
                <td>{entitlementTypeLabel(config.entitlementType)}</td>
                <td>{config.agreementCode}</td>
                <td>{config.okVersion}</td>
                <td className={styles.numeric}>{config.annualQuota}</td>
                <td className={styles.numeric}>{config.carryoverMax}</td>
                <td>{MONTH_LABELS[config.resetMonth] ?? config.resetMonth}</td>
                <td>{formatDate(config.effectiveFrom)}</td>
                <td>
                  <button className={styles.actionBtn} onClick={() => openEdit(config)}>
                    Rediger
                  </button>
                  <button
                    className={styles.deleteBtn}
                    onClick={() => handleDelete(config)}
                    disabled={submitting}
                  >
                    Slet
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {showCreate && (
        <div className={styles.overlay} onClick={closeCreate}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Opret berettigelse</h2>
            <div className={styles.dialogInfo}>
              Gaeldende fra dato saettes til i dag af serveren. Type, aftale, OK-version, akkumuleringsmodel og nulstillingsmaaned er fastlaaste efter oprettelse.
            </div>
            <form onSubmit={handleCreateSubmit}>
              <div className={styles.formGrid}>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-type">
                    Type <span className={styles.required}>*</span>
                  </label>
                  <select
                    className={styles.input}
                    id="ec-type"
                    value={createForm.entitlementType}
                    onChange={setCreateField('entitlementType')}
                  >
                    {TYPE_OPTIONS.map((t) => (
                      <option key={t} value={t}>
                        {TYPE_LABELS[t]}
                      </option>
                    ))}
                  </select>
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-agreement">
                    Aftale <span className={styles.required}>*</span>
                  </label>
                  <input
                    className={styles.input}
                    id="ec-agreement"
                    type="text"
                    required
                    value={createForm.agreementCode}
                    onChange={setCreateField('agreementCode')}
                    placeholder="f.eks. AC"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-okversion">
                    OK-version <span className={styles.required}>*</span>
                  </label>
                  <input
                    className={styles.input}
                    id="ec-okversion"
                    type="text"
                    required
                    value={createForm.okVersion}
                    onChange={setCreateField('okVersion')}
                    placeholder="f.eks. OK24"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-accrual">
                    Akkumuleringsmodel <span className={styles.required}>*</span>
                  </label>
                  <select
                    className={styles.input}
                    id="ec-accrual"
                    value={createForm.accrualModel}
                    onChange={setCreateField('accrualModel')}
                  >
                    {ACCRUAL_OPTIONS.map((m) => (
                      <option key={m} value={m}>
                        {m === 'IMMEDIATE' ? 'Straks' : 'Maanedlig optjening'}
                      </option>
                    ))}
                  </select>
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-resetmonth">
                    Nulstillingsmaaned <span className={styles.required}>*</span>
                  </label>
                  <select
                    className={styles.input}
                    id="ec-resetmonth"
                    value={createForm.resetMonth}
                    onChange={setCreateField('resetMonth')}
                  >
                    {Object.entries(MONTH_LABELS).map(([num, label]) => (
                      <option key={num} value={num}>
                        {label}
                      </option>
                    ))}
                  </select>
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-quota">
                    Aarlig kvote <span className={styles.required}>*</span>
                  </label>
                  <input
                    className={styles.input}
                    id="ec-quota"
                    type="number"
                    required
                    min={0}
                    step={0.01}
                    value={createForm.annualQuota}
                    onChange={setCreateField('annualQuota')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-carryover">
                    Maks overfoersel
                  </label>
                  <input
                    className={styles.input}
                    id="ec-carryover"
                    type="number"
                    min={0}
                    step={0.01}
                    value={createForm.carryoverMax}
                    onChange={setCreateField('carryoverMax')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-minage">
                    Min. alder (valgfri)
                  </label>
                  <input
                    className={styles.input}
                    id="ec-minage"
                    type="number"
                    min={0}
                    step={1}
                    value={createForm.minAge}
                    onChange={setCreateField('minAge')}
                    placeholder="Tom = ingen kraevet alder"
                  />
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="ec-prorate"
                    type="checkbox"
                    checked={createForm.proRateByPartTime}
                    onChange={setCreateField('proRateByPartTime')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="ec-prorate">
                    Pro-rata for deltid
                  </label>
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="ec-perepisode"
                    type="checkbox"
                    checked={createForm.isPerEpisode}
                    onChange={setCreateField('isPerEpisode')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="ec-perepisode">
                    Per episode (ikke kvote)
                  </label>
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="ec-fulldayonly"
                    type="checkbox"
                    checked={createForm.fullDayOnly}
                    onChange={setCreateField('fullDayOnly')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="ec-fulldayonly">
                    Kun hele dage (omsorgs-/seniordage)
                  </label>
                </div>
                <div className={styles.formFieldFull}>
                  <label className={styles.formLabel} htmlFor="ec-desc">
                    Beskrivelse
                  </label>
                  <textarea
                    className={styles.textarea}
                    id="ec-desc"
                    value={createForm.description}
                    onChange={setCreateField('description')}
                    placeholder="Valgfri beskrivelse"
                  />
                </div>
              </div>

              {formError && <div className={styles.alert}>{formError}</div>}

              <div className={styles.dialogActions}>
                <button type="button" className={styles.cancelBtn} onClick={closeCreate}>
                  Annuller
                </button>
                <button type="submit" className={styles.createBtn} disabled={submitting}>
                  {submitting ? 'Opretter...' : 'Opret'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {editing && (
        <div className={styles.overlay} onClick={closeEdit}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Rediger berettigelse</h2>
            <div className={styles.dialogInfo}>
              {entitlementTypeLabel(editing.entitlementType)} / {editing.agreementCode} / {editing.okVersion}
              {' '}— gaeldende fra {formatDate(editing.effectiveFrom)} (version {editing.version}).
              {' '}Naturlige noegle-felter, akkumuleringsmodel og nulstillingsmaaned er fastlaaste per ADR-021.
            </div>
            <form onSubmit={handleEditSubmit}>
              <div className={styles.formGrid}>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    Type
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={entitlementTypeLabel(editing.entitlementType)}
                    readOnly
                    aria-readonly="true"
                    title="Fastlaast per ADR-021 — opret en ny OK-version for at aendre"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    Aftale
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={editing.agreementCode}
                    readOnly
                    aria-readonly="true"
                    title="Fastlaast per ADR-021 — opret en ny OK-version for at aendre"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    OK-version
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={editing.okVersion}
                    readOnly
                    aria-readonly="true"
                    title="Fastlaast per ADR-021 — opret en ny OK-version for at aendre"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    Akkumuleringsmodel
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={accrualModelLabel(editing.accrualModel)}
                    readOnly
                    aria-readonly="true"
                    title="Fastlaast per ADR-021 Q1(i) — opret en ny OK-version for at aendre"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    Nulstillingsmaaned
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={MONTH_LABELS[editing.resetMonth] ?? String(editing.resetMonth)}
                    readOnly
                    aria-readonly="true"
                    title="Fastlaast per ADR-021 Q1(i) — opret en ny OK-version for at aendre"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    Kun hele dage
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={editing.fullDayOnly ? 'Ja' : 'Nej'}
                    readOnly
                    aria-readonly="true"
                    title="Fuld-dags-reglen er en bevidst skema-/ejer-aendring (S73) — kan ikke aendres via dette billede"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-edit-quota">
                    Aarlig kvote <span className={styles.required}>*</span>
                  </label>
                  <input
                    className={styles.input}
                    id="ec-edit-quota"
                    type="number"
                    required
                    min={0}
                    step={0.01}
                    value={editForm.annualQuota}
                    onChange={setEditField('annualQuota')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-edit-carryover">
                    Maks overfoersel
                  </label>
                  <input
                    className={styles.input}
                    id="ec-edit-carryover"
                    type="number"
                    min={0}
                    step={0.01}
                    value={editForm.carryoverMax}
                    onChange={setEditField('carryoverMax')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="ec-edit-minage">
                    Min. alder (valgfri)
                  </label>
                  <input
                    className={styles.input}
                    id="ec-edit-minage"
                    type="number"
                    min={0}
                    step={1}
                    value={editForm.minAge}
                    onChange={setEditField('minAge')}
                    placeholder="Tom = ingen kraevet alder"
                  />
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="ec-edit-prorate"
                    type="checkbox"
                    checked={editForm.proRateByPartTime}
                    onChange={setEditField('proRateByPartTime')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="ec-edit-prorate">
                    Pro-rata for deltid
                  </label>
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="ec-edit-perepisode"
                    type="checkbox"
                    checked={editForm.isPerEpisode}
                    onChange={setEditField('isPerEpisode')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="ec-edit-perepisode">
                    Per episode (ikke kvote)
                  </label>
                </div>
                <div className={styles.formFieldFull}>
                  <label className={styles.formLabel} htmlFor="ec-edit-desc">
                    Beskrivelse
                  </label>
                  <textarea
                    className={styles.textarea}
                    id="ec-edit-desc"
                    value={editForm.description}
                    onChange={setEditField('description')}
                    placeholder="Valgfri beskrivelse"
                  />
                </div>
              </div>

              {formError && <div className={styles.alert}>{formError}</div>}

              <div className={styles.dialogActions}>
                <button type="button" className={styles.cancelBtn} onClick={closeEdit}>
                  Annuller
                </button>
                <button type="submit" className={styles.createBtn} disabled={submitting}>
                  {submitting ? 'Gemmer...' : 'Gem'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
