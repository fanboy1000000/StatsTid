import { useState, useEffect, type FormEvent } from 'react'
import styles from './UserManagement.module.css'
import { useOrganizations, useOrgUsers, type User, type WithEtag } from '../../hooks/useAdmin'
import { useReportingLines, type ReportingLineEntry } from '../../hooks/useReportingLines'
import { useToast } from '../../components/ui/Toast'
import { Spinner } from '../../components/ui'
import { formatVersionAsIfMatch } from '../../lib/etag'

const AGREEMENT_CODES = ['AC', 'HK', 'PROSA'] as const

interface CreateUserForm {
  userId: string
  username: string
  password: string
  displayName: string
  email: string
  primaryOrgId: string
  agreementCode: string
}

interface EditUserForm {
  displayName: string
  email: string
  primaryOrgId: string
  agreementCode: string
}

// S34 TASK-3409 (ADR-023 D8). UTC year-month-day matches the backend's
// `DateTime.UtcNow` reference for the same-day-only-edit validator (TASK-3407).
// Mirrors `EmployeeProfileEditor.tsx` precedent (S33 TASK-3311); using local
// midnight would drift on either side of the IANA boundary.
function todayIsoUtc(): string {
  return new Date().toISOString().slice(0, 10)
}

export function UserManagement() {
  const { organizations, loading: orgsLoading, error: orgsError } = useOrganizations()
  const { toast } = useToast()
  const [selectedOrgId, setSelectedOrgId] = useState('')
  // S35 TASK-3507 — migrated to the shared `useOrgUsers` from `useAdmin.ts`
  // which now routes through `apiFetchWithEtag<T>`; the local raw-fetch hook
  // was removed. The new hook exposes `fetchUser` for capturing the ETag of
  // a single user at edit time so the subsequent PUT can compose `If-Match`.
  const {
    users,
    loading: usersLoading,
    error: usersError,
    fetchUser,
    createUser,
    updateUser,
  } = useOrgUsers(selectedOrgId)

  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  // S35 TASK-3507. The edit dialog holds a `WithEtag<User>` so the `etag`
  // field can be passed straight through as `If-Match` on the PUT, and a
  // post-412 refresh can rebind both the form values and the captured ETag.
  const [editingUser, setEditingUser] = useState<WithEtag<User> | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  // S25/S29/S30 banner-with-retry precedent (see EmployeeProfileEditor.tsx
  // L106-112 + L239-249). A 412 from PUT populates `staleConflict`; the
  // "Genindlaes" button refetches the user (server returns the current ETag),
  // rebinds the edit form, and clears the banner so HR can re-save.
  const [staleConflict, setStaleConflict] = useState<{
    expected?: number
    actual?: number
  } | null>(null)

  // S48 TASK-4810. Reporting-line display in user detail.
  const {
    fetchEmployeeLines,
    assignManager: assignReportingLine,
    removeActingManager,
  } = useReportingLines()
  const [activeLines, setActiveLines] = useState<ReportingLineEntry[]>([])
  const [linesLoading, setLinesLoading] = useState(false)
  const [managerDialogOpen, setManagerDialogOpen] = useState(false)
  const [managerForm, setManagerForm] = useState<{
    managerId: string
    effectiveFrom: string
  }>({ managerId: '', effectiveFrom: '' })
  const [managerSubmitting, setManagerSubmitting] = useState(false)
  const [managerFormError, setManagerFormError] = useState<string | null>(null)

  const [createForm, setCreateForm] = useState<CreateUserForm>({
    userId: '',
    username: '',
    password: '',
    displayName: '',
    email: '',
    primaryOrgId: '',
    agreementCode: 'AC',
  })

  const [editForm, setEditForm] = useState<EditUserForm>({
    displayName: '',
    email: '',
    primaryOrgId: '',
    agreementCode: 'AC',
  })

  // Set default org once loaded
  useEffect(() => {
    if (organizations.length > 0 && !selectedOrgId) {
      setSelectedOrgId(organizations[0].orgId)
    }
  }, [organizations, selectedOrgId])

  const selectedOrg = organizations.find((o) => o.orgId === selectedOrgId)

  const handleOpenCreate = () => {
    setCreateForm({
      userId: '',
      username: '',
      password: '',
      displayName: '',
      email: '',
      primaryOrgId: selectedOrgId,
      agreementCode: selectedOrg?.agreementCode ?? 'AC',
    })
    setFormError(null)
    setCreateDialogOpen(true)
  }

  const handleCloseCreate = () => {
    setCreateDialogOpen(false)
    setFormError(null)
  }

  const handleCreateSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setFormError(null)
    try {
      await createUser({
        userId: createForm.userId,
        username: createForm.username,
        password: createForm.password,
        displayName: createForm.displayName,
        email: createForm.email || undefined,
        primaryOrgId: createForm.primaryOrgId,
        agreementCode: createForm.agreementCode,
      })
      toast({ title: 'Oprettet', description: 'Bruger oprettet', variant: 'success' })
      handleCloseCreate()
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  // S35 TASK-3507. Fetch the per-user row through the new GET endpoint so the
  // ETag is captured before the edit dialog opens. The list-level row carries
  // the `version` body field as well, but going through `fetchUser` keeps the
  // ETag-resolution path in one place and matches the EmployeeProfileEditor
  // precedent (S31 TASK-3109).
  const handleRowClick = async (user: User) => {
    setFormError(null)
    setStaleConflict(null)
    setActiveLines([])
    setEditForm({
      displayName: user.displayName,
      email: user.email ?? '',
      primaryOrgId: user.primaryOrgId,
      agreementCode: user.agreementCode,
    })
    try {
      const fresh = await fetchUser(user.userId)
      setEditingUser(fresh)
      setEditForm({
        displayName: fresh.displayName,
        email: fresh.email ?? '',
        primaryOrgId: fresh.primaryOrgId,
        agreementCode: fresh.agreementCode,
      })
      setEditDialogOpen(true)
      // S48 TASK-4810. Fetch reporting lines for the selected user.
      setLinesLoading(true)
      const linesResult = await fetchEmployeeLines(user.userId)
      if (linesResult.ok) {
        setActiveLines(linesResult.data.active)
      }
      setLinesLoading(false)
    } catch (err) {
      setLinesLoading(false)
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }

  // S48 TASK-4810. Reload reporting lines for the currently editing user.
  const reloadLines = async (userId: string) => {
    setLinesLoading(true)
    const linesResult = await fetchEmployeeLines(userId)
    if (linesResult.ok) {
      setActiveLines(linesResult.data.active)
    }
    setLinesLoading(false)
  }

  const handleCloseEdit = () => {
    setEditDialogOpen(false)
    setEditingUser(null)
    setFormError(null)
    setStaleConflict(null)
    setActiveLines([])
    setManagerDialogOpen(false)
    setManagerFormError(null)
  }

  // S48 TASK-4810. Open "Change Manager" dialog.
  const handleOpenManagerDialog = () => {
    setManagerForm({ managerId: '', effectiveFrom: todayIsoUtc() })
    setManagerFormError(null)
    setManagerDialogOpen(true)
  }

  const handleCloseManagerDialog = () => {
    setManagerDialogOpen(false)
    setManagerFormError(null)
  }

  const handleManagerSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!editingUser) return
    setManagerSubmitting(true)
    setManagerFormError(null)
    try {
      const result = await assignReportingLine({
        employeeId: editingUser.userId,
        managerId: managerForm.managerId,
        effectiveFrom: managerForm.effectiveFrom,
      })
      if (!result.ok) {
        setManagerFormError(result.error)
      } else {
        toast({ title: 'Tildelt', description: 'Leder tildelt', variant: 'success' })
        handleCloseManagerDialog()
        await reloadLines(editingUser.userId)
      }
    } catch (err) {
      setManagerFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setManagerSubmitting(false)
    }
  }

  const handleRemoveActing = async (line: ReportingLineEntry) => {
    if (!editingUser) return
    const ifMatch = formatVersionAsIfMatch(line.version)
    const result = await removeActingManager(line.employeeId, ifMatch)
    if (result.ok) {
      toast({ title: 'Fjernet', description: 'Vikarierende leder fjernet', variant: 'success' })
      await reloadLines(editingUser.userId)
    } else {
      toast({ title: 'Fejl', description: result.error, variant: 'error' })
    }
  }

  /**
   * S35 TASK-3507 banner-with-retry. Re-fetches the user (captures the freshest
   * ETag), rebinds the form to the server-side state, and clears the banner so
   * HR can re-save. Mirrors `EmployeeProfileEditor.handleStaleRefresh`.
   */
  const handleStaleRefresh = async () => {
    if (!editingUser) {
      setStaleConflict(null)
      return
    }
    try {
      const fresh = await fetchUser(editingUser.userId)
      setEditingUser(fresh)
      setEditForm({
        displayName: fresh.displayName,
        email: fresh.email ?? '',
        primaryOrgId: fresh.primaryOrgId,
        agreementCode: fresh.agreementCode,
      })
      setStaleConflict(null)
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleEditSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!editingUser) return
    setSubmitting(true)
    setFormError(null)
    try {
      const updated = await updateUser(
        editingUser.userId,
        {
          // S34 TASK-3409 (ADR-023 D8). Inject today's UTC date so the wire
          // body satisfies TASK-3407's `UpdateUserRequest` required
          // `EffectiveFrom` field. UTC matches the backend's
          // `DateTime.UtcNow` same-day-only-edit validator. No UI affordance —
          // pure wire-shape sync (admin user-edit ergonomics differ from
          // `EmployeeProfileEditor`'s as-of-date toggle).
          effectiveFrom: todayIsoUtc(),
          displayName: editForm.displayName,
          email: editForm.email || undefined,
          primaryOrgId: editForm.primaryOrgId,
          agreementCode: editForm.agreementCode,
        },
        // S35 TASK-3507 (ADR-019 D2 admin-strict If-Match). The freshest
        // ETag from the most recent GET / prior PUT — captured into the
        // `editingUser.etag` field above.
        editingUser.etag,
      )
      // Successful save — rebind to the post-save ETag so a second edit in
      // the same dialog composes If-Match against the new version without a
      // round-trip GET.
      setEditingUser(updated)
      toast({ title: 'Gemt', description: 'Bruger opdateret', variant: 'success' })
      handleCloseEdit()
    } catch (err) {
      // S35 TASK-3507. 412 -> show banner-with-retry; other errors -> generic
      // form-level error message. Mirrors EmployeeProfileEditor.handleMutationError.
      const e2 = err as Error & {
        status?: number
        body?: { expectedVersion?: number; actualVersion?: number }
      }
      if (e2.status === 412) {
        setStaleConflict({
          expected: e2.body?.expectedVersion,
          actual: e2.body?.actualVersion,
        })
      } else {
        setFormError(err instanceof Error ? err.message : String(err))
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Brugere</h1>
      </div>

      {orgsError && <div className={styles.alert}>{orgsError}</div>}

      <div className={styles.orgSelector}>
        <label className={styles.orgSelectorLabel} htmlFor="orgSelect">
          Organisation
        </label>
        {orgsLoading ? (
          <div className={styles.spinner}><Spinner size="lg" /></div>
        ) : (
          <select
            className={styles.select}
            id="orgSelect"
            value={selectedOrgId}
            onChange={(e) => setSelectedOrgId(e.target.value)}
            style={{ maxWidth: 400 }}
          >
            {organizations.map((org) => (
              <option key={org.orgId} value={org.orgId}>
                {org.orgName} ({org.orgId})
              </option>
            ))}
          </select>
        )}
      </div>

      {selectedOrg && (
        <div className={styles.card}>
          <div className={styles.cardHeader}>
            <h2 className={styles.cardHeaderTitle}>{selectedOrg.orgName}</h2>
            <button className={styles.createBtn} onClick={handleOpenCreate}>
              Opret bruger
            </button>
          </div>
          <div className={styles.cardBody}>
            {usersError && <div className={styles.alert}>{usersError}</div>}

            {usersLoading && (
              <div className={styles.spinner}><Spinner size="lg" /></div>
            )}

            {!usersLoading && !usersError && users.length === 0 && (
              <div className={styles.emptyState}>
                Ingen brugere fundet for denne organisation
              </div>
            )}

            {!usersLoading && users.length > 0 && (
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Brugernavn</th>
                    <th>Navn</th>
                    <th>E-mail</th>
                    <th>Organisation</th>
                    <th>Overenskomst</th>
                  </tr>
                </thead>
                <tbody>
                  {users.map((user) => (
                    <tr key={user.userId} onClick={() => handleRowClick(user)}>
                      <td>{user.userId}</td>
                      <td>{user.username}</td>
                      <td>{user.displayName}</td>
                      <td className={styles.emailCell}>
                        {user.email ?? '\u2014'}
                      </td>
                      <td>{user.primaryOrgId}</td>
                      <td>{user.agreementCode}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}

      {/* Create user dialog */}
      {createDialogOpen && (
        <div className={styles.overlay} onClick={handleCloseCreate}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Opret bruger</h2>
            <form onSubmit={handleCreateSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newUserId">
                  Bruger-ID <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="newUserId"
                  type="text"
                  required
                  value={createForm.userId}
                  onChange={(e) =>
                    setCreateForm((f) => ({ ...f, userId: e.target.value }))
                  }
                  placeholder="f.eks. EMP010"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newUsername">
                  Brugernavn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="newUsername"
                  type="text"
                  required
                  value={createForm.username}
                  onChange={(e) =>
                    setCreateForm((f) => ({ ...f, username: e.target.value }))
                  }
                  placeholder="Brugernavn"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newPassword">
                  Adgangskode <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="newPassword"
                  type="password"
                  required
                  value={createForm.password}
                  onChange={(e) =>
                    setCreateForm((f) => ({ ...f, password: e.target.value }))
                  }
                  placeholder="Adgangskode"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newDisplayName">
                  Visningsnavn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="newDisplayName"
                  type="text"
                  required
                  value={createForm.displayName}
                  onChange={(e) =>
                    setCreateForm((f) => ({
                      ...f,
                      displayName: e.target.value,
                    }))
                  }
                  placeholder="Fuldt navn"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newEmail">
                  E-mail
                </label>
                <input
                  className={styles.input}
                  id="newEmail"
                  type="email"
                  value={createForm.email}
                  onChange={(e) =>
                    setCreateForm((f) => ({ ...f, email: e.target.value }))
                  }
                  placeholder="bruger@example.dk"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newPrimaryOrg">
                  Organisation <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="newPrimaryOrg"
                  value={createForm.primaryOrgId}
                  onChange={(e) =>
                    setCreateForm((f) => ({
                      ...f,
                      primaryOrgId: e.target.value,
                    }))
                  }
                >
                  {organizations.map((org) => (
                    <option key={org.orgId} value={org.orgId}>
                      {org.orgName} ({org.orgId})
                    </option>
                  ))}
                </select>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="newAgreement">
                  Overenskomst <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="newAgreement"
                  value={createForm.agreementCode}
                  onChange={(e) =>
                    setCreateForm((f) => ({
                      ...f,
                      agreementCode: e.target.value,
                    }))
                  }
                >
                  {AGREEMENT_CODES.map((code) => (
                    <option key={code} value={code}>
                      {code}
                    </option>
                  ))}
                </select>
              </div>

              {formError && <div className={styles.alert}>{formError}</div>}

              <div className={styles.dialogActions}>
                <button
                  type="button"
                  className={styles.cancelBtn}
                  onClick={handleCloseCreate}
                >
                  Annuller
                </button>
                <button
                  type="submit"
                  className={styles.createBtn}
                  disabled={submitting}
                >
                  {submitting ? 'Opretter...' : 'Opret'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit user dialog */}
      {editDialogOpen && editingUser && (
        <div className={styles.overlay} onClick={handleCloseEdit}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>
              Rediger bruger: {editingUser.username}
            </h2>
            {/* S35 TASK-3507 banner-with-retry per S25/S29/S30 precedent
                (EmployeeProfileEditor.tsx L239-249, EntitlementConfigEditor,
                WageTypeMappingManagement). 412 from PUT shows the banner
                until HR clicks "Genindlaes" which refetches and rebinds. */}
            {staleConflict && (
              <div className={styles.alert} role="alert" data-testid="stale-conflict-banner">
                Brugeren er aendret af en anden administrator siden du indlaeste den.
                {staleConflict.expected !== undefined && staleConflict.actual !== undefined && (
                  <> {' '}(Forventet version {staleConflict.expected}, aktuel version {staleConflict.actual}.)</>
                )}
                {' '}
                <button type="button" className={styles.actionBtn} onClick={handleStaleRefresh}>
                  Genindlaes
                </button>
              </div>
            )}
            <form onSubmit={handleEditSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editDisplayName">
                  Visningsnavn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="editDisplayName"
                  type="text"
                  required
                  value={editForm.displayName}
                  onChange={(e) =>
                    setEditForm((f) => ({
                      ...f,
                      displayName: e.target.value,
                    }))
                  }
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editEmail">
                  E-mail
                </label>
                <input
                  className={styles.input}
                  id="editEmail"
                  type="email"
                  value={editForm.email}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, email: e.target.value }))
                  }
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editPrimaryOrg">
                  Organisation <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="editPrimaryOrg"
                  value={editForm.primaryOrgId}
                  onChange={(e) =>
                    setEditForm((f) => ({
                      ...f,
                      primaryOrgId: e.target.value,
                    }))
                  }
                >
                  {organizations.map((org) => (
                    <option key={org.orgId} value={org.orgId}>
                      {org.orgName} ({org.orgId})
                    </option>
                  ))}
                </select>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editAgreement">
                  Overenskomst <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="editAgreement"
                  value={editForm.agreementCode}
                  onChange={(e) =>
                    setEditForm((f) => ({
                      ...f,
                      agreementCode: e.target.value,
                    }))
                  }
                >
                  {AGREEMENT_CODES.map((code) => (
                    <option key={code} value={code}>
                      {code}
                    </option>
                  ))}
                </select>
              </div>

              {formError && <div className={styles.alert}>{formError}</div>}

              <div className={styles.dialogActions}>
                <button
                  type="button"
                  className={styles.cancelBtn}
                  onClick={handleCloseEdit}
                >
                  Annuller
                </button>
                <button
                  type="submit"
                  className={styles.createBtn}
                  disabled={submitting}
                >
                  {submitting ? 'Gemmer...' : 'Gem'}
                </button>
              </div>
            </form>

            {/* S48 TASK-4810. Reporting-line display below the edit form. */}
            <div style={{ marginTop: 20, borderTop: '1px solid #e0e0e0', paddingTop: 16 }}>
              <div className={styles.formLabel} style={{ fontWeight: 600, fontSize: 14, marginBottom: 8 }}>
                Ledelseslinjer
              </div>
              {linesLoading && <div style={{ fontSize: 13, color: '#666' }}>Indlaeser...</div>}
              {!linesLoading && activeLines.length === 0 && (
                <div style={{ fontSize: 13, color: '#666' }}>Ingen ledelseslinjer registreret</div>
              )}
              {!linesLoading && activeLines.map((line) => {
                const isPrimary = line.relationship === 'PRIMARY'
                return (
                  <div key={line.reportingLineId} style={{ fontSize: 13, marginBottom: 6, display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span>
                      {isPrimary ? 'Naermeste leder' : 'Vikarierende leder'}:{' '}
                      <strong>{line.managerId}</strong>{' '}
                      ({isPrimary ? 'PRIMARY' : 'ACTING'})
                    </span>
                    {isPrimary && (
                      <button
                        type="button"
                        className={styles.actionBtn}
                        onClick={handleOpenManagerDialog}
                      >
                        Skift leder
                      </button>
                    )}
                    {!isPrimary && (
                      <button
                        type="button"
                        className={styles.actionBtn}
                        onClick={() => handleRemoveActing(line)}
                      >
                        Fjern vikar
                      </button>
                    )}
                  </div>
                )
              })}
              {!linesLoading && activeLines.length > 0 && !activeLines.some((l) => l.relationship === 'PRIMARY') && (
                <button
                  type="button"
                  className={styles.actionBtn}
                  onClick={handleOpenManagerDialog}
                  style={{ marginTop: 4 }}
                >
                  Tildel leder
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* S48 TASK-4810. Change manager dialog */}
      {managerDialogOpen && editingUser && (
        <div className={styles.overlay} onClick={handleCloseManagerDialog}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Skift leder</h2>
            <form onSubmit={handleManagerSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="mgrSelectId">
                  Ny leder <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="mgrSelectId"
                  required
                  value={managerForm.managerId}
                  onChange={(e) =>
                    setManagerForm((f) => ({ ...f, managerId: e.target.value }))
                  }
                >
                  <option value="">Vaelg leder...</option>
                  {users
                    .filter((u) => u.userId !== editingUser.userId)
                    .map((u) => (
                      <option key={u.userId} value={u.userId}>
                        {u.displayName} ({u.userId})
                      </option>
                    ))}
                </select>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="mgrEffectiveFrom">
                  Gyldig fra <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="mgrEffectiveFrom"
                  type="date"
                  required
                  value={managerForm.effectiveFrom}
                  onChange={(e) =>
                    setManagerForm((f) => ({ ...f, effectiveFrom: e.target.value }))
                  }
                />
              </div>

              {managerFormError && <div className={styles.alert}>{managerFormError}</div>}

              <div className={styles.dialogActions}>
                <button
                  type="button"
                  className={styles.cancelBtn}
                  onClick={handleCloseManagerDialog}
                >
                  Annuller
                </button>
                <button
                  type="submit"
                  className={styles.createBtn}
                  disabled={managerSubmitting}
                >
                  {managerSubmitting ? 'Gemmer...' : 'Gem'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
