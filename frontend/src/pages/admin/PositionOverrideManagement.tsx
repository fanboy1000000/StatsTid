import { useEffect, useRef, useState, type ChangeEvent, type FormEvent, type RefObject } from 'react'
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
import { formatEffectiveDateLong } from './editPerson/EffectiveDatePicker'
import {
  dateBoundRefusal,
  dateRefusalFrom,
  isCalendarDate,
  laterDate,
  newestKeyDate,
  predecessorFloor,
  type DateRefusal,
} from './nearestMondayOffer'
import styles from './PositionOverrideManagement.module.css'

interface CreateForm {
  // null = untouched (today); a cleared field is '' and disables the confirm.
  appliesFrom: string | null
  agreementCode: string
  okVersion: string
  positionCode: string
  maxFlexBalance: string
  normPeriodWeeks: string
  weeklyNormHours: string
  description: string
}

const emptyForm: CreateForm = {
  appliesFrom: null,
  agreementCode: '',
  okVersion: '',
  positionCode: '',
  maxFlexBalance: '',
  normPeriodWeeks: '',
  weeklyNormHours: '',
  description: '',
}

// The placeholder date the seeded overrides carry (docker/postgres/init.sql); shown as stored, since
// the long form would read "1. januar 1".
const SEEDED_ANCHOR_DATE = '0001-01-01'

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
  // True while the create request is pending: the fields that identify the override are locked, so an
  // answer about one key cannot land in a form that shows another.
  const [creating, setCreating] = useState(false)
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

  // The date of a write: today by default, never later (the server refuses a future date), and
  // never before the newest version of the KEY (agreement, OK version, position): the highest
  // `appliesFrom` among the listed rows of that key, inactive rows included (nearestMondayOffer.ts
  // newestKeyDate). A date out of those bounds is refused here before anything is sent; the server
  // stays the authority on all three date rules and a refusal it still gives (a stale list, a
  // concurrent write) is shown with the dates it would accept. One row is in an edit or a pending
  // switch at a time, so one `rowDate` serves both (null = untouched = today; '' = cleared).
  const today = useCalendarToday()
  const [rowDate, setRowDate] = useState<string | null>(null)
  const [pending, setPending] = useState<{ id: string; kind: 'deactivate' | 'activate' } | null>(null)
  // The server's refusal of a date, one slot for the create form and one for the row step; each is set
  // and cleared only by its own interaction.
  const [createRefusal, setCreateRefusal] = useState<DateRefusal | null>(null)
  const [rowRefusal, setRowRefusal] = useState<DateRefusal | null>(null)
  const clearRefusal = (kind: 'create' | 'row') => (kind === 'create' ? setCreateRefusal(null) : setRowRefusal(null))
  // The error of a row action (edit, switch), shown above the table; a create's error is `formError`,
  // inside the create form.
  const [rowError, setRowError] = useState<string | null>(null)
  const [switching, setSwitching] = useState(false)
  // The date the server last named as the earliest it accepts (a BEFORE_PREDECESSOR answer); it raises
  // the list's bound, never lowers it. One slot for the create form (by its typed key) and one for the
  // row step (by override id); each is cleared only when its own interaction ends.
  const [createFloor, setCreateFloor] = useState<{ scope: string; date: string } | null>(null)
  const [rowFloor, setRowFloor] = useState<{ id: string; date: string } | null>(null)
  // Each interaction (the create form, the row step) has its own generation, bumped only when it opens,
  // cancels or closes. An answer applies when its interaction's generation is unchanged and the date it
  // submitted is still that interaction's date; typing in other fields or acting in the other form
  // does not make it stale.
  const createGen = useRef(0)
  const rowGen = useRef(0)
  const createDateNow = useRef('')
  const rowDateNow = useRef('')
  type Submission = { kind: 'create' | 'row'; gen: number; date: string }
  const stillCurrent = (s: Submission) =>
    s.kind === 'create'
      ? s.gen === createGen.current && s.date === createDateNow.current
      : s.gen === rowGen.current && s.date === rowDateNow.current
  const createDateInput = useRef<HTMLInputElement>(null)
  const rowDateInput = useRef<HTMLInputElement>(null)

  const createDate = form.appliesFrom ?? today
  const createScope = `create|${form.agreementCode}|${form.okVersion}|${form.positionCode}`
  const createBound = laterDate(
    newestKeyDate(data, form),
    createFloor && createFloor.scope === createScope ? createFloor.date : null,
  )
  const createBoundRefusal = dateBoundRefusal(createDate, today, createBound)
  // "Unknown" is not "none": while the list is loading or failed the key's lower limit is unknown.
  const createBlocked = loading || error !== null || !isCalendarDate(createDate) || createBoundRefusal !== null

  const rowItem = data.find((r) => r.overrideId === (editingId ?? pending?.id))
  const rowDateValue = rowDate ?? today
  const rowBound = rowItem
    ? laterDate(newestKeyDate(data, rowItem), rowFloor && rowFloor.id === rowItem.overrideId ? rowFloor.date : null)
    : null
  const rowBoundRefusal = rowItem ? dateBoundRefusal(rowDateValue, today, rowBound) : null
  const rowBlocked = !isCalendarDate(rowDateValue) || rowBoundRefusal !== null
  useEffect(() => {
    createDateNow.current = createDate
    rowDateNow.current = rowDateValue
  })

  const handleChange = (field: keyof CreateForm) => (e: ChangeEvent<HTMLInputElement>) => {
    setForm((f) => ({ ...f, [field]: e.target.value }))
  }

  const changeRowDate = (date: string) => {
    setRowDate(date)
    clearRefusal('row')
  }

  // `key` is the create form's key scope or the override id (the floor's slot); `bound` is the date's
  // lower limit. An answer for an interaction that was cancelled or replaced since, or for a date the
  // user has changed since, is ignored. A failed write leaves the forms as typed; it re-reads the list
  // only after a refusal that names the predecessor's date (below), so the dates shown catch up.
  const handleMutationError = (err: unknown, mine: Submission, key: string, bound: string | null) => {
    if (!stillCurrent(mine)) return
    if (isPositionOverrideMutationError(err) && err.status === 412) {
      setStaleConflict({ expected: err.body?.expectedVersion, actual: err.body?.actualVersion })
      return
    }
    const named = isPositionOverrideMutationError(err) ? predecessorFloor(err) : null
    if (named) {
      if (mine.kind === 'create') setCreateFloor({ scope: key, date: named })
      else setRowFloor({ id: key, date: named })
      // The list is read again so the newest dates it shows catch up with the server's floor.
      void refetch()
    }
    const refusal = isPositionOverrideMutationError(err)
      ? dateRefusalFrom(err, today, laterDate(bound, named))
      : null
    if (refusal) {
      if (mine.kind === 'create') setCreateRefusal(refusal)
      else setRowRefusal(refusal)
    } else {
      const message = err instanceof Error ? err.message : String(err)
      if (mine.kind === 'create') setFormError(message)
      else setRowError(message)
    }
  }

  // The refusal of a scope: the one the screen knows (a date out of bounds) first, else the server's.
  const renderDateRefusal = (
    kind: 'create' | 'row',
    known: DateRefusal | null,
    setDate: (date: string) => void,
    input: RefObject<HTMLInputElement | null>,
  ) => {
    const shown = known ?? (kind === 'create' ? createRefusal : rowRefusal)
    return shown ? (
      <div className={styles.alert} role="alert">
        {shown.text}
        {shown.buttons.map((b) => (
          <button
            key={b.date}
            type="button"
            className={styles.actionBtn}
            onClick={() => { setDate(b.date); clearRefusal(kind); input.current?.focus() }}
          >
            {b.label}
          </button>
        ))}
      </div>
    ) : null
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
    if (createBlocked) return
    const mine: Submission = { kind: 'create', gen: createGen.current, date: createDate }
    setSubmitting(true)
    setCreating(true)
    setFormError(null)
    clearRefusal('create')
    try {
      await create({
        appliesFrom: createDate,
        agreementCode: form.agreementCode,
        okVersion: form.okVersion,
        positionCode: form.positionCode,
        maxFlexBalance: parseOptionalNumber(form.maxFlexBalance),
        normPeriodWeeks: parseOptionalNumber(form.normPeriodWeeks),
        weeklyNormHours: parseOptionalNumber(form.weeklyNormHours),
        description: form.description || null,
      })
      if (stillCurrent(mine)) {
        resetCreate()
        setShowCreate(false)
      }
    } catch (err) {
      handleMutationError(err, mine, createScope, createBound)
    } finally {
      setSubmitting(false)
      setCreating(false)
    }
  }

  // The create form's interaction ends (cancelled, or closed by a successful create): its error,
  // refusal and floor go with it.
  const resetCreate = () => {
    createGen.current += 1
    setFormError(null)
    clearRefusal('create')
    setCreateFloor(null)
    setForm(emptyForm)
  }

  // The row step's interaction ends: its refusal, error and floor go with it.
  const resetRowStep = () => {
    rowGen.current += 1
    clearRefusal('row')
    setRowError(null)
    setRowFloor(null)
  }

  const startEdit = (item: WithEtag<PositionOverrideConfig>) => {
    resetRowStep()
    setPending(null)
    setRowDate(null)
    setEditingId(item.overrideId)
    setEditForm({
      maxFlexBalance: item.maxFlexBalance?.toString() ?? '',
      normPeriodWeeks: item.normPeriodWeeks?.toString() ?? '',
      weeklyNormHours: item.weeklyNormHours?.toString() ?? '',
      description: item.description ?? '',
    })
  }

  const cancelEdit = () => {
    resetRowStep()
    setEditingId(null)
    setEditForm({})
  }

  // A switch on or off asks for its date first (no longer one click): it can change the weekly norm
  // in force, so it obeys the same date rules as an edit.
  const startSwitch = (item: WithEtag<PositionOverrideConfig>, kind: 'deactivate' | 'activate') => {
    cancelEdit()
    setRowDate(null)
    setPending({ id: item.overrideId, kind })
  }

  const cancelSwitch = () => {
    resetRowStep()
    setPending(null)
  }

  const saveEdit = async (item: WithEtag<PositionOverrideConfig>) => {
    if (rowBlocked) return
    const mine: Submission = { kind: 'row', gen: rowGen.current, date: rowDateValue }
    setSubmitting(true)
    clearRefusal('row')
    setRowError(null)
    try {
      await update(item.overrideId, item.etag, {
        appliesFrom: rowDateValue,
        agreementCode: item.agreementCode,
        okVersion: item.okVersion,
        positionCode: item.positionCode,
        maxFlexBalance: parseOptionalNumber(editForm.maxFlexBalance ?? ''),
        normPeriodWeeks: parseOptionalNumber(editForm.normPeriodWeeks ?? ''),
        weeklyNormHours: parseOptionalNumber(editForm.weeklyNormHours ?? ''),
        description: editForm.description || null,
      })
      if (stillCurrent(mine)) cancelEdit()
    } catch (err) {
      handleMutationError(err, mine, item.overrideId, rowBound)
    } finally {
      setSubmitting(false)
    }
  }

  const confirmSwitch = async (item: WithEtag<PositionOverrideConfig>, kind: 'deactivate' | 'activate') => {
    if (rowBlocked || switching) return
    const mine: Submission = { kind: 'row', gen: rowGen.current, date: rowDateValue }
    setSwitching(true)
    clearRefusal('row')
    setRowError(null)
    try {
      await (kind === 'deactivate' ? deactivate : activate)(item.overrideId, item.etag, rowDateValue)
      if (stillCurrent(mine)) cancelSwitch()
    } catch (err) {
      handleMutationError(err, mine, item.overrideId, rowBound)
    } finally {
      setSwitching(false)
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
              resetCreate()
              setShowCreate(!showCreate)
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
                  disabled={creating}
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
                  disabled={creating}
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
                  disabled={creating}
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
                  ref={createDateInput}
                  value={createDate}
                  max={today}
                  min={createBound ?? undefined}
                  onChange={(e) => { handleChange('appliesFrom')(e); clearRefusal('create') }}
                />
              </div>
            </div>

            {renderDateRefusal(
              'create',
              createBoundRefusal,
              (date) => setForm((f) => ({ ...f, appliesFrom: date })),
              createDateInput,
            )}
            {formError && <div className={styles.alert}>{formError}</div>}

            <div className={styles.formActions}>
              <button
                type="button"
                className={styles.cancelBtn}
                onClick={() => { resetCreate(); setShowCreate(false) }}
              >
                Annuller
              </button>
              <button type="submit" className={styles.createBtn} disabled={submitting || createBlocked}>
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

      {rowError && <div className={styles.alert} role="alert">{rowError}</div>}
      {rowItem && renderDateRefusal('row', rowBoundRefusal, changeRowDate, rowDateInput)}

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
                        ref={rowDateInput}
                        value={rowDateValue}
                        max={today}
                        min={rowBound ?? undefined}
                        onChange={(e) => changeRowDate(e.target.value)}
                      />
                    ) : item.appliesFrom === SEEDED_ANCHOR_DATE ? (
                      item.appliesFrom
                    ) : (
                      item.appliesFrom ? formatEffectiveDateLong(item.appliesFrom) : '—'
                    )}
                  </td>
                  {canWrite && (
                  <td>
                    {isEditing ? (
                      <>
                        <button
                          className={styles.saveBtn}
                          onClick={() => saveEdit(item)}
                          disabled={submitting || rowBlocked}
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
                          ref={rowDateInput}
                          value={rowDateValue}
                          max={today}
                          min={rowBound ?? undefined}
                          onChange={(e) => changeRowDate(e.target.value)}
                        />
                        <button
                          className={pending.kind === 'deactivate' ? styles.deactivateBtn : styles.activateBtn}
                          onClick={() => confirmSwitch(item, pending.kind)}
                          disabled={switching || rowBlocked}
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
