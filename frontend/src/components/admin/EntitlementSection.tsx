import { useState, type ChangeEvent, type FormEvent } from 'react'
import { apiFetchWithEtag } from '../../lib/api'
import { formatVersionAsIfMatch } from '../../lib/etag'
import type { EntitlementType, AccrualModel } from '../../lib/entitlementConstants'
import {
  TYPE_LABELS,
  TYPE_OPTIONS,
  ACCRUAL_OPTIONS,
  ACCRUAL_LABELS,
  MONTH_LABELS,
} from '../../lib/entitlementConstants'
import styles from './EntitlementSection.module.css'

// TASK-1B-3: Inline entitlements section for the agreement config editor.
// Sub-resource endpoints:
//   GET    /api/agreement-configs/{configId}/entitlements
//   POST   /api/agreement-configs/{configId}/entitlements
//   PUT    /api/agreement-configs/{configId}/entitlements/{eid}  (If-Match required)
//   DELETE /api/agreement-configs/{configId}/entitlements/{eid}  (If-Match required)

export interface Entitlement {
  configId: string
  entitlementType: EntitlementType
  agreementCode: string
  okVersion: string
  annualQuota: number
  accrualModel: AccrualModel
  resetMonth: number
  carryoverMax: number
  proRateByPartTime: boolean
  isPerEpisode: boolean
  minAge: number | null
  description: string | null
  effectiveFrom: string
  effectiveTo: string | null
  version: number
}

interface EntitlementSectionProps {
  configId: string
  entitlements: Entitlement[]
  readOnly: boolean
  onRefresh: () => void
}

interface EntitlementFormState {
  entitlementType: EntitlementType
  annualQuota: string
  accrualModel: AccrualModel
  resetMonth: string
  carryoverMax: string
  proRateByPartTime: boolean
  isPerEpisode: boolean
  minAge: string
  description: string
}

const emptyForm: EntitlementFormState = {
  entitlementType: 'VACATION',
  annualQuota: '0',
  accrualModel: 'IMMEDIATE',
  resetMonth: '1',
  carryoverMax: '0',
  proRateByPartTime: false,
  isPerEpisode: false,
  minAge: '',
  description: '',
}

function entitlementToForm(e: Entitlement): EntitlementFormState {
  return {
    entitlementType: e.entitlementType,
    annualQuota: String(e.annualQuota),
    accrualModel: e.accrualModel,
    resetMonth: String(e.resetMonth),
    carryoverMax: String(e.carryoverMax),
    proRateByPartTime: e.proRateByPartTime,
    isPerEpisode: e.isPerEpisode,
    minAge: e.minAge === null ? '' : String(e.minAge),
    description: e.description ?? '',
  }
}

function parseNum(value: string, fallback: number): number {
  const parsed = Number.parseFloat(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parseOptionalInt(value: string): number | null {
  if (!value.trim()) return null
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : null
}

function formToCreateBody(f: EntitlementFormState) {
  return {
    entitlementType: f.entitlementType,
    annualQuota: parseNum(f.annualQuota, 0),
    accrualModel: f.accrualModel,
    resetMonth: Number.parseInt(f.resetMonth, 10) || 1,
    carryoverMax: parseNum(f.carryoverMax, 0),
    proRateByPartTime: f.proRateByPartTime,
    isPerEpisode: f.isPerEpisode,
    minAge: parseOptionalInt(f.minAge),
    description: f.description.trim() || null,
  }
}

function formToUpdateBody(f: EntitlementFormState) {
  return {
    entitlementType: f.entitlementType,
    annualQuota: parseNum(f.annualQuota, 0),
    accrualModel: f.accrualModel,
    resetMonth: Number.parseInt(f.resetMonth, 10) || 1,
    carryoverMax: parseNum(f.carryoverMax, 0),
    proRateByPartTime: f.proRateByPartTime,
    isPerEpisode: f.isPerEpisode,
    minAge: parseOptionalInt(f.minAge),
    description: f.description.trim() || null,
  }
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('da-DK')
  } catch {
    return iso
  }
}

/**
 * Classify an API error into an actionable message.
 * 409 = read-only conflict, 412 = stale version, 428 = missing If-Match.
 */
function classifyError(status: number, body: unknown): string {
  if (status === 412) {
    return 'Data er aendret. Genindlaes og proev igen.'
  }
  if (status === 428) {
    return 'Manglende versionstjek (If-Match). Genindlaes siden.'
  }
  if (status === 409) {
    return 'Berettigelser er skrivebeskyttede for denne konfiguration (delt aftale-kode/OK-version).'
  }
  if (typeof body === 'object' && body !== null && 'error' in body) {
    return String((body as { error: string }).error)
  }
  return `Fejl (HTTP ${status})`
}

export function EntitlementSection({ configId, entitlements, readOnly, onRefresh }: EntitlementSectionProps) {
  const [showCreate, setShowCreate] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<EntitlementFormState>(emptyForm)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const basePath = `/api/agreement-configs/${configId}/entitlements`

  const setField = <K extends keyof EntitlementFormState>(key: K) =>
    (e: ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
      const target = e.target as HTMLInputElement
      const value: EntitlementFormState[K] =
        target.type === 'checkbox'
          ? (target.checked as EntitlementFormState[K])
          : (target.value as EntitlementFormState[K])
      setForm((f) => ({ ...f, [key]: value }))
    }

  const openCreate = () => {
    setForm(emptyForm)
    setError(null)
    setSuccess(null)
    setShowCreate(true)
    setEditingId(null)
  }

  const openEdit = (ent: Entitlement) => {
    setForm(entitlementToForm(ent))
    setError(null)
    setSuccess(null)
    setEditingId(ent.configId)
    setShowCreate(false)
  }

  const closeDialog = () => {
    setShowCreate(false)
    setEditingId(null)
    setForm(emptyForm)
    setError(null)
  }

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    try {
      const result = await apiFetchWithEtag<Entitlement>(basePath, {
        method: 'POST',
        body: JSON.stringify(formToCreateBody(form)),
      })
      if (!result.ok) {
        setError(classifyError(result.status, result.body))
      } else {
        setSuccess('Berettigelse oprettet')
        closeDialog()
        onRefresh()
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleUpdate = async (e: FormEvent) => {
    e.preventDefault()
    if (!editingId) return
    const ent = entitlements.find((x) => x.configId === editingId)
    if (!ent) return
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    try {
      const ifMatch = formatVersionAsIfMatch(ent.version)
      const result = await apiFetchWithEtag<Entitlement>(`${basePath}/${editingId}`, {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(formToUpdateBody(form)),
      })
      if (!result.ok) {
        setError(classifyError(result.status, result.body))
      } else {
        setSuccess('Berettigelse opdateret')
        closeDialog()
        onRefresh()
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleDelete = async (ent: Entitlement) => {
    const label = TYPE_LABELS[ent.entitlementType]
    const ok = window.confirm(`Slet berettigelsen "${label}"?`)
    if (!ok) return
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    try {
      const ifMatch = formatVersionAsIfMatch(ent.version)
      const result = await apiFetchWithEtag<void>(`${basePath}/${ent.configId}`, {
        method: 'DELETE',
        headers: { 'If-Match': ifMatch },
      })
      if (!result.ok) {
        setError(classifyError(result.status, result.body))
      } else {
        setSuccess('Berettigelse slettet')
        onRefresh()
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const editingEntitlement = editingId ? entitlements.find((x) => x.configId === editingId) : null
  // When editing, frozen fields come from the entitlement being edited
  const isEditMode = editingId !== null && editingEntitlement !== null

  return (
    <div className={styles.section}>
      <div className={styles.sectionHeader}>
        <h3 className={styles.sectionTitle}>Berettigelser</h3>
        {readOnly ? (
          <span className={styles.readOnlyNote} title="Berettigelser er skrivebeskyttede naar flere overenskomster deler denne aftale-kode og OK-version">
            Skrivebeskyttet (delt aftale-kode/OK-version)
          </span>
        ) : (
          <button className={styles.addBtn} onClick={openCreate} disabled={submitting}>
            Tilfoej berettigelse
          </button>
        )}
      </div>

      {error && <div className={styles.alert}>{error}</div>}
      {success && <div className={styles.success}>{success}</div>}

      {entitlements.length === 0 ? (
        <div className={styles.emptyState}>Ingen berettigelser konfigureret for denne overenskomst</div>
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Type</th>
              <th>Akkumulering</th>
              <th className={styles.numeric}>Aarlig kvote</th>
              <th className={styles.numeric}>Overfoersel</th>
              <th>Nulstilling</th>
              <th>Fra</th>
              {!readOnly && <th>Handlinger</th>}
            </tr>
          </thead>
          <tbody>
            {entitlements.map((ent) => (
              <tr key={ent.configId}>
                <td>{TYPE_LABELS[ent.entitlementType] ?? ent.entitlementType}</td>
                <td>{ACCRUAL_LABELS[ent.accrualModel] ?? ent.accrualModel}</td>
                <td className={styles.numeric}>{ent.annualQuota}</td>
                <td className={styles.numeric}>{ent.carryoverMax}</td>
                <td>{MONTH_LABELS[ent.resetMonth] ?? ent.resetMonth}</td>
                <td>{formatDate(ent.effectiveFrom)}</td>
                {!readOnly && (
                  <td>
                    <button className={styles.actionBtn} onClick={() => openEdit(ent)} disabled={submitting}>
                      Rediger
                    </button>
                    <button className={styles.deleteBtn} onClick={() => handleDelete(ent)} disabled={submitting}>
                      Slet
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* Create dialog */}
      {showCreate && (
        <div className={styles.overlay} onClick={closeDialog}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Tilfoej berettigelse</h2>
            <div className={styles.dialogInfo}>
              Gaeldende fra-dato saettes af serveren. Type, akkumuleringsmodel og nulstillingsmaaned er fastlaaste efter oprettelse.
            </div>
            <form onSubmit={handleCreate}>
              <div className={styles.formGrid}>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-type">
                    Type <span className={styles.required}>*</span>
                  </label>
                  <select
                    className={styles.input}
                    id="es-type"
                    value={form.entitlementType}
                    onChange={setField('entitlementType')}
                  >
                    {TYPE_OPTIONS.map((t) => (
                      <option key={t} value={t}>{TYPE_LABELS[t]}</option>
                    ))}
                  </select>
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-accrual">
                    Akkumuleringsmodel <span className={styles.required}>*</span>
                  </label>
                  <select
                    className={styles.input}
                    id="es-accrual"
                    value={form.accrualModel}
                    onChange={setField('accrualModel')}
                  >
                    {ACCRUAL_OPTIONS.map((m) => (
                      <option key={m} value={m}>{ACCRUAL_LABELS[m]}</option>
                    ))}
                  </select>
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-resetmonth">
                    Nulstillingsmaaned <span className={styles.required}>*</span>
                  </label>
                  <select
                    className={styles.input}
                    id="es-resetmonth"
                    value={form.resetMonth}
                    onChange={setField('resetMonth')}
                  >
                    {Object.entries(MONTH_LABELS).map(([num, label]) => (
                      <option key={num} value={num}>{label}</option>
                    ))}
                  </select>
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-quota">
                    Aarlig kvote <span className={styles.required}>*</span>
                  </label>
                  <input
                    className={styles.input}
                    id="es-quota"
                    type="number"
                    required
                    min={0}
                    step={0.01}
                    value={form.annualQuota}
                    onChange={setField('annualQuota')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-carryover">
                    Maks overfoersel
                  </label>
                  <input
                    className={styles.input}
                    id="es-carryover"
                    type="number"
                    min={0}
                    step={0.01}
                    value={form.carryoverMax}
                    onChange={setField('carryoverMax')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-minage">
                    Min. alder (valgfri)
                  </label>
                  <input
                    className={styles.input}
                    id="es-minage"
                    type="number"
                    min={0}
                    step={1}
                    value={form.minAge}
                    onChange={setField('minAge')}
                    placeholder="Tom = ingen kraevet alder"
                  />
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="es-prorate"
                    type="checkbox"
                    checked={form.proRateByPartTime}
                    onChange={setField('proRateByPartTime')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="es-prorate">
                    Pro-rata for deltid
                  </label>
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="es-perepisode"
                    type="checkbox"
                    checked={form.isPerEpisode}
                    onChange={setField('isPerEpisode')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="es-perepisode">
                    Per episode (ikke kvote)
                  </label>
                </div>
                <div className={styles.formFieldFull}>
                  <label className={styles.formLabel} htmlFor="es-desc">
                    Beskrivelse
                  </label>
                  <textarea
                    className={styles.textarea}
                    id="es-desc"
                    value={form.description}
                    onChange={setField('description')}
                    placeholder="Valgfri beskrivelse"
                  />
                </div>
              </div>

              {error && <div className={styles.alert}>{error}</div>}

              <div className={styles.dialogActions}>
                <button type="button" className={styles.cancelBtn} onClick={closeDialog}>
                  Annuller
                </button>
                <button type="submit" className={styles.saveBtn} disabled={submitting}>
                  {submitting ? 'Opretter...' : 'Opret'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit dialog */}
      {isEditMode && editingEntitlement && (
        <div className={styles.overlay} onClick={closeDialog}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Rediger berettigelse</h2>
            <div className={styles.dialogInfo}>
              {TYPE_LABELS[editingEntitlement.entitlementType]} — gaeldende fra {formatDate(editingEntitlement.effectiveFrom)} (version {editingEntitlement.version}).
              Type, akkumuleringsmodel og nulstillingsmaaned er fastlaaste.
            </div>
            <form onSubmit={handleUpdate}>
              <div className={styles.formGrid}>
                <div className={styles.formField}>
                  <label className={styles.formLabel}>
                    Type
                    <span className={styles.frozenHint}>(fastlaast)</span>
                  </label>
                  <input
                    className={`${styles.input} ${styles.readOnly}`}
                    type="text"
                    value={TYPE_LABELS[editingEntitlement.entitlementType]}
                    readOnly
                    aria-readonly="true"
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
                    value={ACCRUAL_LABELS[editingEntitlement.accrualModel] ?? editingEntitlement.accrualModel}
                    readOnly
                    aria-readonly="true"
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
                    value={MONTH_LABELS[editingEntitlement.resetMonth] ?? String(editingEntitlement.resetMonth)}
                    readOnly
                    aria-readonly="true"
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-edit-quota">
                    Aarlig kvote <span className={styles.required}>*</span>
                  </label>
                  <input
                    className={styles.input}
                    id="es-edit-quota"
                    type="number"
                    required
                    min={0}
                    step={0.01}
                    value={form.annualQuota}
                    onChange={setField('annualQuota')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-edit-carryover">
                    Maks overfoersel
                  </label>
                  <input
                    className={styles.input}
                    id="es-edit-carryover"
                    type="number"
                    min={0}
                    step={0.01}
                    value={form.carryoverMax}
                    onChange={setField('carryoverMax')}
                  />
                </div>
                <div className={styles.formField}>
                  <label className={styles.formLabel} htmlFor="es-edit-minage">
                    Min. alder (valgfri)
                  </label>
                  <input
                    className={styles.input}
                    id="es-edit-minage"
                    type="number"
                    min={0}
                    step={1}
                    value={form.minAge}
                    onChange={setField('minAge')}
                    placeholder="Tom = ingen kraevet alder"
                  />
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="es-edit-prorate"
                    type="checkbox"
                    checked={form.proRateByPartTime}
                    onChange={setField('proRateByPartTime')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="es-edit-prorate">
                    Pro-rata for deltid
                  </label>
                </div>
                <div className={styles.checkboxField}>
                  <input
                    className={styles.checkbox}
                    id="es-edit-perepisode"
                    type="checkbox"
                    checked={form.isPerEpisode}
                    onChange={setField('isPerEpisode')}
                  />
                  <label className={styles.checkboxLabel} htmlFor="es-edit-perepisode">
                    Per episode (ikke kvote)
                  </label>
                </div>
                <div className={styles.formFieldFull}>
                  <label className={styles.formLabel} htmlFor="es-edit-desc">
                    Beskrivelse
                  </label>
                  <textarea
                    className={styles.textarea}
                    id="es-edit-desc"
                    value={form.description}
                    onChange={setField('description')}
                    placeholder="Valgfri beskrivelse"
                  />
                </div>
              </div>

              {error && <div className={styles.alert}>{error}</div>}

              <div className={styles.dialogActions}>
                <button type="button" className={styles.cancelBtn} onClick={closeDialog}>
                  Annuller
                </button>
                <button type="submit" className={styles.saveBtn} disabled={submitting}>
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
