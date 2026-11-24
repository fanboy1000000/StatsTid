import { useState, type ChangeEvent, type FormEvent } from 'react'
import {
  usePositionOverrides,
  isPositionOverrideMutationError,
  type PositionOverrideConfig,
  type WithEtag,
} from '../../hooks/usePositionOverrides'
import { Spinner } from '../../components/ui'
import { useAuth } from '../../contexts/AuthContext'
import { useCalendarToday } from '../../contexts/CalendarContext'
import { hasMinRole } from '../../lib/roles'
import { dateRefusalFrom, type DateRefusal } from './nearestMondayOffer'
import styles from './PositionOverrideManagement.module.css'

interface CreateForm {
  appliesFrom: string
  agreementCode: string
  okVersion: string
  positionCode: string
  maxFlexBalance: string
  normPeriodWeeks: string
  weeklyNormHours: string
  description: string
}

// An empty `appliesFrom` means today (the date field shows today until the administrator changes it).
const emptyForm: CreateForm = {
  appliesFrom: '',
  agreementCode: '',
  okVersion: '',
  positionCode: '',
  maxFlexBalance: '',
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

  // Every write is GlobalAdminOnly on the server (SEC-032); a local administrator reaches the page
  // for the list, so the write actions are not offered to them (the server would answer 403).
  const { role } = useAuth()
  const canWrite = hasMinRole(role, 'GlobalAdmin')

  // The date of a write: today by default, never later (the server refuses a future date), and for
  // a change to an existing key never before its newest version (the row's own appliesFrom). The
  // server stays the authority on all three date rules; a refusal is shown with the dates it would
  // accept (nearestMondayOffer.ts). One row is in an edit or a pending switch at a time, so one
  // `rowDate` serves both ('' = today).
  const today = useCalendarToday()
  const [rowDate, setRowDate] = useState('')
  const [pending, setPending] = useState<{ id: string; kind: 'deactivate' | 'activate' } | null>(null)
  const [dateRefusal, setDateRefusal] = useState<{ scope: string; refusal: DateRefusal } | null>(null)

  const handleChange = (field: keyof CreateForm) => (e: ChangeEvent<HTMLInputElement>) => {
    setForm((f) => ({ ...f, [field]: e.target.value }))
  }

  // `scope` is 'create' or the override id; `newest` is the key's newest version date (null for create).
  const handleMutationError = (err: unknown, scope: string, newest: string | null) => {
    if (isPositionOverrideMutationError(err) && err.status === 412) {
      setStaleConflict({ expected: err.body?.expectedVersion, actual: err.body?.actualVersion })
      return
    }
    const refusal = isPositionOverrideMutationError(err) ? dateRefusalFrom(err, today, newest) : null
    if (refusal) {
      setDateRefusal({ scope, refusal })
    } else {
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }

  const renderDateRefusal = (scope: string, setDate: (date: string) => void) =>
    dateRefusal && dateRefusal.scope === scope ? (
      <div className={styles.alert} role="alert">
        {dateRefusal.refusal.text}
        {dateRefusal.refusal.buttons.map((b) => (
          <button
            key={b.date}
            type="button"
            className={styles.actionBtn}
            onClick={() => { setDate(b.date); setDateRefusal(null) }}
          >
            {b.label}
          </button>
        ))}
      </div>
    ) : null

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
    setDateRefusal(null)
    try {
      await create({
        appliesFrom: form.appliesFrom || today,
        agreementCode: form.agreementCode,
        okVersion: form.okVersion,
        positionCode: form.positionCode,
        maxFlexBalance: parseOptionalNumber(form.maxFlexBalance),
        normPeriodWeeks: parseOptionalNumber(form.normPeriodWeeks),
        weeklyNormHours: parseOptionalNumber(form.weeklyNormHours),
        description: form.description || null,
      })
      setForm(emptyForm)
      setShowCreate(false)
    } catch (err) {
      handleMutationError(err, 'create', null)
    } finally {
      setSubmitting(false)
    }
  }

  const startEdit = (item: WithEtag<PositionOverrideConfig>) => {
    setPending(null)
    setDateRefusal(null)
    setRowDate('')
    setEditingId(item.overrideId)
    setEditForm({
      maxFlexBalance: item.maxFlexBalance?.toString() ?? '',
      normPeriodWeeks: item.normPeriodWeeks?.toString() ?? '',
      weeklyNormHours: item.weeklyNormHours?.toString() ?? '',
      description: item.description ?? '',
    })
  }

  const cancelEdit = () => {
    setEditingId(null)
    setEditForm({})
    setDateRefusal(null)
  }

  // A switch on or off asks for its date first (no longer one click): it can change the weekly norm
  // in force, so it obeys the same date rules as an edit.
  const startSwitch = (item: WithEtag<PositionOverrideConfig>, kind: 'deactivate' | 'activate') => {
    cancelEdit()
    setRowDate('')
    setPending({ id: item.overrideId, kind })
  }

  const cancelSwitch = () => {
    setPending(null)
    setDateRefusal(null)
  }

  const saveEdit = async (item: WithEtag<PositionOverrideConfig>) => {
    setSubmitting(true)
    setDateRefusal(null)
    try {
      await update(item.overrideId, item.etag, {
        appliesFrom: rowDate || today,
        agreementCode: item.agreementCode,
        okVersion: item.okVersion,
        positionCode: item.positionCode,
        maxFlexBalance: parseOptionalNumber(editForm.maxFlexBalance ?? ''),
        normPeriodWeeks: parseOptionalNumber(editForm.normPeriodWeeks ?? ''),
        weeklyNormHours: parseOptionalNumber(editForm.weeklyNormHours ?? ''),
        description: editForm.description || null,
      })
      setEditingId(null)
      setEditForm({})
    } catch (err) {
      handleMutationError(err, item.overrideId, item.appliesFrom)
    } finally {
      setSubmitting(false)
    }
  }

  const confirmSwitch = async (item: WithEtag<PositionOverrideConfig>, kind: 'deactivate' | 'activate') => {
    setDateRefusal(null)
    try {
      await (kind === 'deactivate' ? deactivate : activate)(item.overrideId, item.etag, rowDate || today)
      setPending(null)
    } catch (err) {
      handleMutationError(err, item.overrideId, item.appliesFrom)
    }
  }

  const handleEditChange = (field: string) => (e: ChangeEvent<HTMLInputElement>) => {
    setEditForm((f) => ({ ...f, [field]: e.target.value }))
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Positionstilpasninger</h1>
        {canWrite && (
          <button
            className={styles.createBtn}
            onClick={() => {
              setShowCreate(!showCreate)
              setFormError(null)
              setDateRefusal(null)
              setForm(emptyForm)
            }}
          >
            {showCreate ? 'Annuller' : 'Opret ny'}
          </button>
        )}
      </div>

      {!canWrite && (
        <div className={styles.emptyState}>Tilpasningerne kan kun ændres af en global administrator.</div>
      )}

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

      {canWrite && showCreate && (
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
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="po-appliesfrom">
                  Gælder fra
                </label>
                <input
                  className={styles.input}
                  id="po-appliesfrom"
                  type="date"
                  value={form.appliesFrom || today}
                  max={today}
                  onChange={handleChange('appliesFrom')}
                />
              </div>
            </div>

            {renderDateRefusal('create', (date) => setForm((f) => ({ ...f, appliesFrom: date })))}
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
        <div className={styles.emptyState}>Ingen positionstilpasninger fundet</div>
      )}

      {dateRefusal && dateRefusal.scope !== 'create' &&
        renderDateRefusal(dateRefusal.scope, setRowDate)}

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
              <th>Gælder fra</th>
              {canWrite && <th>Handlinger</th>}
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
                      <input
                        className={styles.inlineInput}
                        type="date"
                        aria-label="Ændringen gælder fra"
                        value={rowDate || today}
                        max={today}
                        min={item.appliesFrom ?? undefined}
                        onChange={(e) => setRowDate(e.target.value)}
                      />
                    ) : (
                      item.appliesFrom ?? '—'
                    )}
                  </td>
                  {canWrite && (
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
                    ) : pending?.id === item.overrideId ? (
                      <>
                        <label htmlFor={`po-switch-${item.overrideId}`}>
                          {pending.kind === 'deactivate' ? 'Deaktiveres fra' : 'Aktiveres fra'}
                        </label>
                        <input
                          className={styles.inlineInput}
                          id={`po-switch-${item.overrideId}`}
                          type="date"
                          value={rowDate || today}
                          max={today}
                          min={item.appliesFrom ?? undefined}
                          onChange={(e) => setRowDate(e.target.value)}
                        />
                        <button
                          className={pending.kind === 'deactivate' ? styles.deactivateBtn : styles.activateBtn}
                          onClick={() => confirmSwitch(item, pending.kind)}
                        >
                          {pending.kind === 'deactivate' ? 'Deaktiver' : 'Aktiver'}
                        </button>
                        <button className={styles.actionBtn} onClick={cancelSwitch}>
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
                            onClick={() => startSwitch(item, 'deactivate')}
                          >
                            Deaktiver
                          </button>
                        ) : (
                          <button
                            className={styles.activateBtn}
                            onClick={() => startSwitch(item, 'activate')}
                          >
                            Aktiver
                          </button>
                        )}
                      </>
                    )}
                  </td>
                  )}
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
