import { useState, type ChangeEvent, type FormEvent } from 'react'
import { useWageTypeMappings, type WageTypeMappingItem, type WithEtag } from '../../hooks/useWageTypeMappings'
import { Spinner } from '../../components/ui'
import styles from './WageTypeMappingManagement.module.css'

interface CreateForm {
  timeType: string
  wageType: string
  okVersion: string
  agreementCode: string
  position: string
  description: string
}

const emptyForm: CreateForm = {
  timeType: '',
  wageType: '',
  okVersion: '',
  agreementCode: '',
  position: '',
  description: '',
}

function mappingKey(m: WageTypeMappingItem): string {
  return `${m.timeType}|${m.okVersion}|${m.agreementCode}|${m.position}`
}

export function WageTypeMappingManagement() {
  const { data, loading, error, refetch, create, updateMapping, deleteMapping } = useWageTypeMappings()
  const [showCreate, setShowCreate] = useState(false)
  const [form, setForm] = useState<CreateForm>(emptyForm)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [editForm, setEditForm] = useState<{ wageType: string; description: string }>({ wageType: '', description: '' })
  // S25 / TASK-2506 (ADR-019 pending) banner-with-retry precedent
  // (mirrors ProfileEditor.tsx:135). 412 from update / delete sets
  // `staleConflict`; the "Genindlaes" button refetches the list and clears it.
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
    if (!form.timeType || !form.wageType || !form.okVersion || !form.agreementCode) {
      setFormError('Udfyld venligst alle paakraevede felter.')
      return
    }
    setSubmitting(true)
    setFormError(null)
    try {
      await create({
        timeType: form.timeType,
        wageType: form.wageType,
        okVersion: form.okVersion,
        agreementCode: form.agreementCode,
        position: form.position,
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

  const startEdit = (item: WithEtag<WageTypeMappingItem>) => {
    setEditingKey(mappingKey(item))
    setEditForm({
      wageType: item.wageType,
      description: item.description ?? '',
    })
  }

  const cancelEdit = () => {
    setEditingKey(null)
    setEditForm({ wageType: '', description: '' })
  }

  const saveEdit = async (item: WithEtag<WageTypeMappingItem>) => {
    setSubmitting(true)
    try {
      await updateMapping(item.etag, {
        timeType: item.timeType,
        wageType: editForm.wageType,
        okVersion: item.okVersion,
        agreementCode: item.agreementCode,
        position: item.position,
        description: editForm.description || null,
      })
      cancelEdit()
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  const handleDelete = async (item: WithEtag<WageTypeMappingItem>) => {
    if (!window.confirm(`Slet tilknytning for ${item.timeType} (${item.agreementCode} ${item.okVersion})?`)) {
      return
    }
    try {
      await deleteMapping(item.timeType, item.okVersion, item.agreementCode, item.position, item.etag)
    } catch (err) {
      handleMutationError(err)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Lonartstilknytninger</h1>
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
          <h2 className={styles.createFormTitle}>Opret lonartstilknytning</h2>
          <form onSubmit={handleCreate}>
            <div className={styles.formGrid}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="wt-timetype">
                  Tidstype <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="wt-timetype"
                  type="text"
                  required
                  value={form.timeType}
                  onChange={handleChange('timeType')}
                  placeholder="f.eks. NORMAL_WORK"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="wt-wagetype">
                  Lonart <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="wt-wagetype"
                  type="text"
                  required
                  value={form.wageType}
                  onChange={handleChange('wageType')}
                  placeholder="f.eks. SLS_0100"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="wt-okversion">
                  OK Version <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="wt-okversion"
                  type="text"
                  required
                  value={form.okVersion}
                  onChange={handleChange('okVersion')}
                  placeholder="f.eks. OK24"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="wt-agreement">
                  Aftale <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="wt-agreement"
                  type="text"
                  required
                  value={form.agreementCode}
                  onChange={handleChange('agreementCode')}
                  placeholder="f.eks. AC"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="wt-position">
                  Stilling
                </label>
                <input
                  className={styles.input}
                  id="wt-position"
                  type="text"
                  value={form.position}
                  onChange={handleChange('position')}
                  placeholder="Tom = generel"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="wt-desc">
                  Beskrivelse
                </label>
                <input
                  className={styles.input}
                  id="wt-desc"
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

      {loading && <div className={styles.spinner}><Spinner size="lg" /></div>}

      {!loading && !error && data.length === 0 && (
        <div className={styles.emptyState}>Ingen lonartstilknytninger fundet</div>
      )}

      {!loading && data.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Tidstype</th>
              <th>Lonart</th>
              <th>OK Version</th>
              <th>Aftale</th>
              <th>Stilling</th>
              <th>Beskrivelse</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {data.map((item) => {
              const key = mappingKey(item)
              const isEditing = editingKey === key
              return (
                <tr key={key}>
                  <td>{item.timeType}</td>
                  <td>
                    {isEditing ? (
                      <input
                        className={styles.inlineInput}
                        type="text"
                        value={editForm.wageType}
                        onChange={(e) => setEditForm((f) => ({ ...f, wageType: e.target.value }))}
                      />
                    ) : (
                      item.wageType
                    )}
                  </td>
                  <td>{item.okVersion}</td>
                  <td>{item.agreementCode}</td>
                  <td>
                    {item.position ? (
                      item.position
                    ) : (
                      <span className={styles.generalBadge}>(generel)</span>
                    )}
                  </td>
                  <td>
                    {isEditing ? (
                      <input
                        className={styles.inlineInput}
                        type="text"
                        value={editForm.description}
                        onChange={(e) => setEditForm((f) => ({ ...f, description: e.target.value }))}
                        style={{ width: '140px' }}
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
                        <button className={styles.deleteBtn} onClick={() => handleDelete(item)}>
                          Slet
                        </button>
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
