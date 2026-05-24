import { useState, type ChangeEvent, type FormEvent } from 'react'
import {
  useEntitlementConfigList,
  useEntitlementConfigActions,
  type EntitlementConfig,
  type EntitlementConfigPatch,
  type EntitlementConfigCreateRequest,
  type EntitlementType,
  type AccrualModel,
  type WithEtag,
} from '../../hooks/useEntitlementConfig'
import { Spinner } from '../../components/ui'
import styles from './EntitlementConfigEditor.module.css'

// TASK-3009 / Phase 4d-2 / ADR-021 pending: admin CRUD UI for entitlement
// configs. Mirrors S25 admin pages (WageTypeMappingManagement /
// PositionOverrideManagement) for the table + 412 banner-with-retry shape;
// mirrors S29 ProfileEditor for the same-day-only-edit semantics by NOT
// exposing an effective_from picker (per Q4 + ADR-021 §3, the server stamps
// effective_from = today and same-day edits in-place vs cross-day supersede).

type EntitlementTypeLabel = Record<EntitlementType, string>
const TYPE_LABELS: EntitlementTypeLabel = {
  VACATION: 'Ferie',
  SPECIAL_HOLIDAY: 'Saerlig feriedag',
  CARE_DAY: 'Omsorgsdag',
  CHILD_SICK: 'Barnets sygedag',
  SENIOR_DAY: 'Seniordag',
}

const TYPE_OPTIONS: EntitlementType[] = [
  'VACATION',
  'SPECIAL_HOLIDAY',
  'CARE_DAY',
  'CHILD_SICK',
  'SENIOR_DAY',
]

const ACCRUAL_OPTIONS: AccrualModel[] = ['IMMEDIATE', 'MONTHLY_ACCRUAL']

const MONTH_LABELS: Record<number, string> = {
  1: 'Januar',
  2: 'Februar',
  3: 'Marts',
  4: 'April',
  5: 'Maj',
  6: 'Juni',
  7: 'Juli',
  8: 'August',
  9: 'September',
  10: 'Oktober',
  11: 'November',
  12: 'December',
}

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

  const handleMutationError = (err: unknown) => {
    const e = err as Error & {
      status?: number
      body?: {
        expectedVersion?: number
        actualVersion?: number
        error?: string
        immutable?: string[]
      }
    }
    if (e.status === 412) {
      setStaleConflict({
        expected: e.body?.expectedVersion,
        actual: e.body?.actualVersion,
      })
    } else if (e.status === 422 && e.body?.immutable) {
      setFormError(
        `Felterne ${e.body.immutable.join(', ')} er fastlaast og kan ikke aendres via dette administratorbillede. Opret en ny OK-version i stedet.`,
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
    const label = `${TYPE_LABELS[config.entitlementType]} (${config.agreementCode} ${config.okVersion})`
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

  const setCreateField =
    <K extends keyof CreateFormState>(field: K) =>
    (e: ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
      const target = e.target as HTMLInputElement
      const value: CreateFormState[K] =
        target.type === 'checkbox'
          ? (target.checked as CreateFormState[K])
          : (target.value as CreateFormState[K])
      setCreateForm((f) => ({ ...f, [field]: value }))
    }

  const setEditField =
    <K extends keyof EditFormState>(field: K) =>
    (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      const target = e.target as HTMLInputElement
      const value: EditFormState[K] =
        target.type === 'checkbox'
          ? (target.checked as EditFormState[K])
          : (target.value as EditFormState[K])
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
                <td>{TYPE_LABELS[config.entitlementType] ?? config.entitlementType}</td>
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
              {TYPE_LABELS[editing.entitlementType]} / {editing.agreementCode} / {editing.okVersion}
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
                    value={TYPE_LABELS[editing.entitlementType]}
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
                    value={editing.accrualModel === 'IMMEDIATE' ? 'Straks' : 'Maanedlig optjening'}
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
