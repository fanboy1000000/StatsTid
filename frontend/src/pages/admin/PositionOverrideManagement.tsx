import { useState, type ChangeEvent, type FormEvent } from 'react'
import { usePositionOverrides, type PositionOverrideConfig, type WithEtag } from '../../hooks/usePositionOverrides'
import styles from './PositionOverrideManagement.module.css'

interface CreateForm {
  agreementCode: string
  okVersion: string
  positionCode: string
  maxFlexBalance: string
  flexCarryoverMax: string
  normPeriodWeeks: string
  weeklyNormHours: string
  description: string
}

const emptyForm: CreateForm = {
  agreementCode: '',
  okVersion: '',
  positionCode: '',
  maxFlexBalance: '',
  flexCarryoverMax: '',
  normPeriodWeeks: '',
  weeklyNormHours: '',
  description: '',
}

function parseOptionalNumber(value: string): number | null {
  if (!value.trim()) return null
  const num = Number(value)
  return isNaN(num) ? null : num
}

export function PositionOverrideManagement() {
  const { data, loading, error, refetch, create, update, deactivate, activate } = usePositionOverrides()
  const [showCreate, setShowCreate] = useState(false)
  const [form, setForm] = useState<CreateForm>(emptyForm)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editForm, setEditForm] = useState<Partial<CreateForm>>({})
  // S25 / TASK-2506 (ADR-019 pending) banner-with-retry precedent
  // (mirrors ProfileEditor.tsx:135). 412 from update / activate / deactivate
  // sets `staleConflict`; the "Genindlaes" button refetches the list and
  // clears the banner.
  const [staleConflict, setStaleConflict] = useState<{ expected?: number; actual?: number } | null>(null)

  const handleChange = (field: keyof CreateForm) => (e: ChangeEvent<HTMLInputElement>) => {
    setForm((f) => ({ ...f, [field]: e.target.value }))
  }

  const handleMutationError = (err: unknown) => {
    const e = err as Error & { status?: number; body?: { expectedVersion?: number; actualVersion?: number } }
    if (e.status === 412) {
      setStaleConflict({ expected: e.body?.expectedVersion, actual: e.body?.actualVersion })
    } else {
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleStaleRefresh = async () => {
    setStaleConflict(null)
    await refetch()
  }

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    if (!form.agreementCode || !form.okVersion || !form.positionCode) {
      setFormError('Udfyld venligst alle paakraevede felter.')
      return
    }
    setSubmitting(true)
    setFormError(null)
    try {
      await create({
        agreementCode: form.agreementCode,
        okVersion: form.okVersion,
        positionCode: form.positionCode,
        maxFlexBalance: parseOptionalNumber(form.maxFlexBalance),
        flexCarryoverMax: parseOptionalNumber(form.flexCarryoverMax),
        normPeriodWeeks: parseOptionalNumber(form.normPeriodWeeks),
        weeklyNormHours: parseOptionalNumber(form.weeklyNormHours),
        description: form.description || null,
      })
      setForm(emptyForm)
      setShowCreate(false)
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  const startEdit = (item: WithEtag<PositionOverrideConfig>) => {
    setEditingId(item.overrideId)
    setEditForm({
      maxFlexBalance: item.maxFlexBalance?.toString() ?? '',
      flexCarryoverMax: item.flexCarryoverMax?.toString() ?? '',
      normPeriodWeeks: item.normPeriodWeeks?.toString() ?? '',
      weeklyNormHours: item.weeklyNormHours?.toString() ?? '',
      description: item.description ?? '',
    })
  }

  const cancelEdit = () => {
    setEditingId(null)
    setEditForm({})
  }

  const saveEdit = async (item: WithEtag<PositionOverrideConfig>) => {
    setSubmitting(true)
    try {
      await update(item.overrideId, item.etag, {
        agreementCode: item.agreementCode,
        okVersion: item.okVersion,
        positionCode: item.positionCode,
        maxFlexBalance: parseOptionalNumber(editForm.maxFlexBalance ?? ''),
        flexCarryoverMax: parseOptionalNumber(editForm.flexCarryoverMax ?? ''),
        normPeriodWeeks: parseOptionalNumber(editForm.normPeriodWeeks ?? ''),
        weeklyNormHours: parseOptionalNumber(editForm.weeklyNormHours ?? ''),
        description: editForm.description || null,
      })
      setEditingId(null)
      setEditForm({})
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  const handleDeactivate = async (item: WithEtag<PositionOverrideConfig>) => {
    try {
      await deactivate(item.overrideId, item.etag)
    } catch (err) {
      handleMutationError(err)
    }
  }

  const handleActivate = async (item: WithEtag<PositionOverrideConfig>) => {
    try {
      await activate(item.overrideId, item.etag)
    } catch (err) {
      handleMutationError(err)
    }
  }

  const handleEditChange = (field: string) => (e: ChangeEvent<HTMLInputElement>) => {
    setEditForm((f) => ({ ...f, [field]: e.target.value }))
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Positionstilpasninger</h1>
        <button
          className={styles.createBtn}
          onClick={() => {
            setShowCreate(!showCreate)
            setFormError(null)
            setForm(emptyForm)
          }}
        >
          {showCreate ? 'Annuller' : 'Opret ny'}
        </button>
      </div>

      {staleConflict && (
        <div className={styles.alert} role="alert" data-testid="stale-conflict-banner">
          Din handling var baseret paa en foraeldet tilstand. Listen er blevet opdateret siden.
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

      {showCreate && (
        <div className={styles.createForm}>
          <h2 className={styles.createFormTitle}>Opret positionstilpasning</h2>
          <form onSubmit={handleCreate}>
            <div className={styles.formGrid}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-agreement">
                  Aftale <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="po-agreement"
                  type="text"
                  required
                  value={form.agreementCode}
                  onChange={handleChange('agreementCode')}
                  placeholder="f.eks. AC"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-okversion">
                  OK Version <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="po-okversion"
                  type="text"
                  required
                  value={form.okVersion}
                  onChange={handleChange('okVersion')}
                  placeholder="f.eks. OK24"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-position">
                  Stillingskode <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="po-position"
                  type="text"
                  required
                  value={form.positionCode}
                  onChange={handleChange('positionCode')}
                  placeholder="f.eks. SPECIAL01"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-maxflex">
                  Maks flexsaldo
                </label>
                <input
                  className={styles.input}
                  id="po-maxflex"
                  type="number"
                  step="any"
                  value={form.maxFlexBalance}
                  onChange={handleChange('maxFlexBalance')}
                  placeholder="Timer"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-carryover">
                  Flex overfoersel maks
                </label>
                <input
                  className={styles.input}
                  id="po-carryover"
                  type="number"
                  step="any"
                  value={form.flexCarryoverMax}
                  onChange={handleChange('flexCarryoverMax')}
                  placeholder="Timer"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-normweeks">
                  Normperiode (uger)
                </label>
                <input
                  className={styles.input}
                  id="po-normweeks"
                  type="number"
                  step="1"
                  value={form.normPeriodWeeks}
                  onChange={handleChange('normPeriodWeeks')}
                  placeholder="f.eks. 4"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-weeklynorm">
                  Ugentlig normtid
                </label>
                <input
                  className={styles.input}
                  id="po-weeklynorm"
                  type="number"
                  step="any"
                  value={form.weeklyNormHours}
                  onChange={handleChange('weeklyNormHours')}
                  placeholder="Timer"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-desc">
                  Beskrivelse
                </label>
                <input
                  className={styles.input}
                  id="po-desc"
                  type="text"
                  value={form.description}
                  onChange={handleChange('description')}
                  placeholder="Valgfri beskrivelse"
                />
              </div>
            </div>

            {formError && <div className={styles.alert}>{formError}</div>}

            <div className={styles.formActions}>
              <button
                type="button"
                className={styles.cancelBtn}
                onClick={() => setShowCreate(false)}
              >
                Annuller
              </button>
              <button type="submit" className={styles.createBtn} disabled={submitting}>
                {submitting ? 'Opretter...' : 'Opret'}
              </button>
            </div>
          </form>
        </div>
      )}

      {loading && <div className={styles.spinner}>Henter positionstilpasninger...</div>}

      {!loading && !error && data.length === 0 && (
        <div className={styles.emptyState}>Ingen positionstilpasninger fundet</div>
      )}

      {!loading && data.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Aftale</th>
              <th>OK Version</th>
              <th>Stillingskode</th>
              <th>Status</th>
              <th>Maks flexsaldo</th>
              <th>Normperiode (uger)</th>
              <th>Ugentlig normtid</th>
              <th>Beskrivelse</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {data.map((item) => {
              const isEditing = editingId === item.overrideId
              return (
                <tr key={item.overrideId}>
                  <td>{item.agreementCode}</td>
                  <td>{item.okVersion}</td>
                  <td>{item.positionCode}</td>
                  <td>
                    <span className={item.status === 'ACTIVE' ? styles.badgeActive : styles.badgeInactive}>
                      {item.status === 'ACTIVE' ? 'Aktiv' : 'Inaktiv'}
                    </span>
                  </td>
                  <td>
                    {isEditing ? (
                      <input
                        className={styles.inlineInput}
                        type="number"
                        step="any"
                        value={editForm.maxFlexBalance ?? ''}
                        onChange={handleEditChange('maxFlexBalance')}
                      />
                    ) : (
                      item.maxFlexBalance ?? '—'
                    )}
                  </td>
                  <td>
                    {isEditing ? (
                      <input
                        className={styles.inlineInput}
                        type="number"
                        step="1"
                        value={editForm.normPeriodWeeks ?? ''}
                        onChange={handleEditChange('normPeriodWeeks')}
                      />
                    ) : (
                      item.normPeriodWeeks ?? '—'
                    )}
                  </td>
                  <td>
                    {isEditing ? (
                      <input
                        className={styles.inlineInput}
                        type="number"
                        step="any"
                        value={editForm.weeklyNormHours ?? ''}
                        onChange={handleEditChange('weeklyNormHours')}
                      />
                    ) : (
                      item.weeklyNormHours ?? '—'
                    )}
                  </td>
                  <td>
                    {isEditing ? (
                      <input
                        className={styles.inlineInput}
                        type="text"
                        value={editForm.description ?? ''}
                        onChange={handleEditChange('description')}
                        style={{ width: '120px' }}
                      />
                    ) : (
                      item.description ?? '—'
                    )}
                  </td>
                  <td>
                    {isEditing ? (
                      <>
                        <button
                          className={styles.saveBtn}
                          onClick={() => saveEdit(item)}
                          disabled={submitting}
                        >
                          Gem
                        </button>
                        <button className={styles.actionBtn} onClick={cancelEdit}>
                          Annuller
                        </button>
                      </>
                    ) : (
                      <>
                        <button className={styles.actionBtn} onClick={() => startEdit(item)}>
                          Rediger
                        </button>
                        {item.status === 'ACTIVE' ? (
                          <button
                            className={styles.deactivateBtn}
                            onClick={() => handleDeactivate(item)}
                          >
                            Deaktiver
                          </button>
                        ) : (
                          <button
                            className={styles.activateBtn}
                            onClick={() => handleActivate(item)}
                          >
                            Aktiver
                          </button>
                        )}
                      </>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
