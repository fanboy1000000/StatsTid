import { useState, useEffect, type FormEvent } from 'react'
import styles from './UserManagement.module.css'
import { useOrganizations, useOrgUsers, type User, type WithEtag } from '../../hooks/useAdmin'
import { useReportingLines, type ReportingLineEntry } from '../../hooks/useReportingLines'
import { useEntitlementEligibility } from '../../hooks/useEntitlementEligibility'
import { useToast } from '../../components/ui/Toast'
import { Spinner } from '../../components/ui'
import { apiFetchWithEtag } from '../../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../../lib/etag'

const AGREEMENT_CODES = ['AC', 'HK', 'PROSA'] as const

// S53 TASK-5306e. Employee profile fields inlined into UserManagement after
// EmployeeProfileEditor page removal. The backend employee_profiles table
// stays as invisible plumbing — when HR saves changes here, a separate PUT
// to `/api/admin/employee-profiles/{employeeId}` persists the profile fields.
interface EmployeeProfileSnapshot {
  employeeId: string
  partTimeFraction: number
  position: string | null
  isPartTime: boolean
  version: number
  etag: string
}

async function fetchEmployeeProfile(employeeId: string): Promise<EmployeeProfileSnapshot | null> {
  const result = await apiFetchWithEtag<{
    employeeId: string
    weeklyNormHours: number
    partTimeFraction: number
    position: string | null
    isPartTime: boolean
    version: number
  }>(`/api/admin/employee-profiles/${encodeURIComponent(employeeId)}`)
  if (!result.ok) return null
  const { data, etag } = result.data
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    employeeId: data.employeeId,
    partTimeFraction: data.partTimeFraction,
    position: data.position,
    isPartTime: data.isPartTime,
    version: data.version,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
  }
}

async function saveEmployeeProfile(
  employeeId: string,
  ifMatch: string,
  body: { effectiveFrom: string; partTimeFraction: number; position: string | null },
): Promise<EmployeeProfileSnapshot> {
  // The backend PUT requires weeklyNormHours in the body even though it is
  // being phased out. Send 0 as a placeholder — the backend ignores it for
  // domain logic but the DTO shape requires it.
  const wireBody = {
    effectiveFrom: body.effectiveFrom,
    weeklyNormHours: 0,
    partTimeFraction: body.partTimeFraction,
    position: body.position,
  }
  const result = await apiFetchWithEtag<{
    employeeId: string
    weeklyNormHours: number
    partTimeFraction: number
    position: string | null
    isPartTime: boolean
    version: number
  }>(`/api/admin/employee-profiles/${encodeURIComponent(employeeId)}`, {
    method: 'PUT',
    headers: { 'If-Match': ifMatch },
    body: JSON.stringify(wireBody),
  })
  if (!result.ok) {
    const err = new Error(result.error) as Error & {
      status: number
      body?: unknown
    }
    err.status = result.status
    err.body = result.body
    throw err
  }
  const { data, etag } = result.data
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    employeeId: data.employeeId,
    partTimeFraction: data.partTimeFraction,
    position: data.position,
    isPartTime: data.isPartTime,
    version: data.version,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
  }
}

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
  // S53 TASK-5306e. Profile fields surfaced on the user-edit dialog.
  partTimeFraction: string
  position: string
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

  // S59 TASK-5908 (ADR-029). HR-only per-employee entitlement controls surfaced
  // on the user-edit dialog: (B) CHILD_SICK eligibility opt-in toggle, and
  // (A) date of birth which drives the age-derived SENIOR_DAY gate automatically.
  const {
    fetchChildSickEligibility,
    setChildSick,
    fetchBirthDate,
    setBirthDate,
    fetchEmploymentStartDate,
    setEmploymentStartDate,
  } = useEntitlementEligibility()
  // S59 follow-up: eligibility now has an HR-only GET. On dialog open we read it
  // to pre-populate the toggle AND capture rowExists + version, so the save
  // composes the correct precondition (If-Match when a row exists, else
  // If-None-Match: *). `childSickRowExists` / `childSickVersion` carry that read
  // (and are re-stamped read-your-write after a successful write). `childSickDirty`
  // tracks whether HR changed the toggle so an untouched dialog never writes a
  // spurious eligibility row.
  const [childSickEligible, setChildSickEligible] = useState(false)
  const [childSickRowExists, setChildSickRowExists] = useState(false)
  const [childSickVersion, setChildSickVersion] = useState<number | null>(null)
  const [childSickDirty, setChildSickDirty] = useState(false)
  // DOB is readable (HR-only GET) so the field pre-populates. `birthDate` is the
  // ISO yyyy-MM-dd string (or '' for none); `birthDateVersion` carries
  // users.version for the admin-strict If-Match PUT.
  const [birthDate, setBirthDateValue] = useState('')
  const [birthDateInitial, setBirthDateInitial] = useState('')
  const [birthDateVersion, setBirthDateVersion] = useState<number | null>(null)
  // S60 TASK-6007 (ADR-030). HR-only employment-start date (mirrors DOB):
  // readable yyyy-MM-dd ('' for none) with `employmentStartVersion` carrying
  // users.version for the admin-strict If-Match PUT. Pro-rates mid-year-hire
  // vacation accrual.
  const [employmentStartDate, setEmploymentStartValue] = useState('')
  const [employmentStartInitial, setEmploymentStartInitial] = useState('')
  const [employmentStartVersion, setEmploymentStartVersion] = useState<number | null>(null)
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
    partTimeFraction: '1.000',
    position: '',
  })
  // S53 TASK-5306e. Holds the employee profile snapshot for the user being edited.
  // Fetched alongside the per-user GET when the edit dialog opens. Used for
  // If-Match on the profile PUT and for pre-populating the form fields.
  const [editingProfile, setEditingProfile] = useState<EmployeeProfileSnapshot | null>(null)
  // Per-user profile map for showing part-time fraction in the table without
  // N+1 GETs. Populated lazily the first time a user list loads for the org.
  const [profileMap, setProfileMap] = useState<Record<string, EmployeeProfileSnapshot>>({})

  // Set default org once loaded
  useEffect(() => {
    if (organizations.length > 0 && !selectedOrgId) {
      setSelectedOrgId(organizations[0].orgId)
    }
  }, [organizations, selectedOrgId])

  // S53 TASK-5306e. Fetch employee profiles for all loaded users so the table
  // "Deltid" column can render without N+1 GET-on-row-render. The fetch runs
  // in parallel per user; failures are silently swallowed (the column shows
  // a dash instead). The map resets when the org changes (users changes).
  useEffect(() => {
    if (users.length === 0) {
      setProfileMap({})
      return
    }
    let cancelled = false
    async function loadProfiles() {
      const entries = await Promise.all(
        users.map(async (u) => {
          const p = await fetchEmployeeProfile(u.userId)
          return [u.userId, p] as const
        }),
      )
      if (cancelled) return
      const map: Record<string, EmployeeProfileSnapshot> = {}
      for (const [uid, p] of entries) {
        if (p) map[uid] = p
      }
      setProfileMap(map)
    }
    void loadProfiles()
    return () => { cancelled = true }
  }, [users])

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
    setEditingProfile(null)
    // S59 TASK-5908. Reset per-employee entitlement controls to the opt-in
    // default; the live eligibility + DOB are repopulated from the parallel
    // fetch below.
    setChildSickEligible(false)
    setChildSickRowExists(false)
    setChildSickVersion(null)
    setChildSickDirty(false)
    setBirthDateValue('')
    setBirthDateInitial('')
    setBirthDateVersion(null)
    // S60 TASK-6007. Reset employment-start before the parallel fetch repopulates.
    setEmploymentStartValue('')
    setEmploymentStartInitial('')
    setEmploymentStartVersion(null)
    setEditForm({
      displayName: user.displayName,
      email: user.email ?? '',
      primaryOrgId: user.primaryOrgId,
      agreementCode: user.agreementCode,
      partTimeFraction: '1.000',
      position: '',
    })
    try {
      // Fetch user + employee profile + DOB + employment-start + CHILD_SICK
      // eligibility in parallel.
      const [fresh, profile, dob, employmentStart, elig] = await Promise.all([
        fetchUser(user.userId),
        fetchEmployeeProfile(user.userId),
        fetchBirthDate(user.userId).catch(() => null),
        fetchEmploymentStartDate(user.userId).catch(() => null),
        fetchChildSickEligibility(user.userId).catch(() => null),
      ])
      setEditingUser(fresh)
      setEditingProfile(profile)
      if (dob) {
        setBirthDateValue(dob.birthDate ?? '')
        setBirthDateInitial(dob.birthDate ?? '')
        setBirthDateVersion(dob.version)
      }
      // S60 TASK-6007: pre-populate employment-start + capture users.version for
      // the admin-strict If-Match write on save.
      if (employmentStart) {
        setEmploymentStartValue(employmentStart.employmentStartDate ?? '')
        setEmploymentStartInitial(employmentStart.employmentStartDate ?? '')
        setEmploymentStartVersion(employmentStart.version)
      }
      // S59 follow-up: pre-populate the toggle from the live row and capture
      // rowExists + version for the read-then-If-Match write on save.
      if (elig) {
        setChildSickEligible(elig.eligible)
        setChildSickRowExists(elig.rowExists)
        setChildSickVersion(elig.version)
      }
      setEditForm({
        displayName: fresh.displayName,
        email: fresh.email ?? '',
        primaryOrgId: fresh.primaryOrgId,
        agreementCode: fresh.agreementCode,
        partTimeFraction: profile ? profile.partTimeFraction.toFixed(3) : '1.000',
        position: profile?.position ?? '',
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
    setEditingProfile(null)
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
      const [fresh, profile] = await Promise.all([
        fetchUser(editingUser.userId),
        fetchEmployeeProfile(editingUser.userId),
      ])
      setEditingUser(fresh)
      setEditingProfile(profile)
      setEditForm({
        displayName: fresh.displayName,
        email: fresh.email ?? '',
        primaryOrgId: fresh.primaryOrgId,
        agreementCode: fresh.agreementCode,
        partTimeFraction: profile ? profile.partTimeFraction.toFixed(3) : '1.000',
        position: profile?.position ?? '',
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
      // S53 TASK-5306e. Save user fields and profile fields. The user PUT and
      // the profile PUT are independent — the user PUT goes first; if the
      // profile PUT fails the user changes are already committed (acceptable
      // because profile is a secondary store). If there is no profile snapshot
      // (edge case: profile not found on initial fetch), skip the profile PUT.
      const updated = await updateUser(
        editingUser.userId,
        {
          effectiveFrom: todayIsoUtc(),
          displayName: editForm.displayName,
          email: editForm.email || undefined,
          primaryOrgId: editForm.primaryOrgId,
          agreementCode: editForm.agreementCode,
        },
        editingUser.etag,
      )
      setEditingUser(updated)

      // Profile PUT — only if we have a profile snapshot (ETag) to compose
      // the If-Match against. parseFloat discipline per S31 EmployeeProfileEditor.
      if (editingProfile) {
        const ptf = Number.parseFloat(editForm.partTimeFraction)
        const parsedPtf = Number.isFinite(ptf) ? ptf : 1.0
        const positionTrimmed = editForm.position.trim()
        const updatedProfile = await saveEmployeeProfile(
          editingUser.userId,
          editingProfile.etag,
          {
            effectiveFrom: todayIsoUtc(),
            partTimeFraction: parsedPtf,
            position: positionTrimmed || null,
          },
        )
        setEditingProfile(updatedProfile)
        // Update the profile map so the table column reflects the save immediately.
        setProfileMap((prev) => ({ ...prev, [editingUser.userId]: updatedProfile }))
      }

      // S59 TASK-5908 (A). DOB write — only when HR changed it. Admin-strict
      // If-Match composed from users.version. Read-your-write: re-stamp local
      // state from the PUT response so a follow-up save in the same session
      // carries the bumped version. '' (no DOB) maps to null on the wire.
      if (birthDate !== birthDateInitial && birthDateVersion !== null) {
        const savedDob = await setBirthDate(
          editingUser.userId,
          birthDate || null,
          birthDateVersion,
        )
        setBirthDateValue(savedDob.birthDate ?? '')
        setBirthDateInitial(savedDob.birthDate ?? '')
        setBirthDateVersion(savedDob.version)
      }

      // S60 TASK-6007 (ADR-030). Employment-start write — only when HR changed
      // it. Admin-strict If-Match composed from users.version. Read-your-write:
      // re-stamp local state from the PUT response so a follow-up save in the
      // same session carries the bumped version. '' (no date) maps to null.
      if (
        employmentStartDate !== employmentStartInitial &&
        employmentStartVersion !== null
      ) {
        const savedStart = await setEmploymentStartDate(
          editingUser.userId,
          employmentStartDate || null,
          employmentStartVersion,
        )
        setEmploymentStartValue(savedStart.employmentStartDate ?? '')
        setEmploymentStartInitial(savedStart.employmentStartDate ?? '')
        setEmploymentStartVersion(savedStart.version)
      }

      // S59 TASK-5908 (B) + S59 follow-up. CHILD_SICK eligibility — only when HR
      // touched the toggle (childSickDirty), so an untouched dialog never writes a
      // spurious eligibility row. Read-then-If-Match: a row that already existed
      // (childSickRowExists, version from the dialog-open GET) updates with
      // If-Match; an absent row creates with If-None-Match: *. Read-your-write:
      // re-stamp rowExists + version from the PUT response.
      if (childSickDirty) {
        const savedElig = await setChildSick(
          editingUser.userId,
          childSickEligible,
          childSickRowExists,
          childSickVersion,
        )
        setChildSickEligible(savedElig.eligible)
        setChildSickRowExists(savedElig.rowExists)
        setChildSickVersion(savedElig.version)
        setChildSickDirty(false)
      }

      toast({ title: 'Gemt', description: 'Bruger opdateret', variant: 'success' })
      handleCloseEdit()
    } catch (err) {
      const e2 = err as Error & {
        status?: number
        body?: { expectedVersion?: number; actualVersion?: number; currentVersion?: number }
      }
      if (e2.status === 412) {
        setStaleConflict({
          expected: e2.body?.expectedVersion,
          actual: e2.body?.actualVersion,
        })
      } else if (e2.status === 409) {
        // S59 follow-up: lost-update on the eligibility create (If-None-Match: *
        // raced an existing row). Re-read so the toggle now carries rowExists +
        // version, surface a clear message, and let HR re-save with If-Match.
        if (editingUser) {
          try {
            const elig = await fetchChildSickEligibility(editingUser.userId)
            setChildSickEligible(elig.eligible)
            setChildSickRowExists(elig.rowExists)
            setChildSickVersion(elig.version)
            setChildSickDirty(false)
          } catch {
            // Re-read failed too; the message below still tells HR to retry.
          }
        }
        setFormError(err instanceof Error ? err.message : String(err))
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
                    <th>Deltid</th>
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
                      <td>
                        {profileMap[user.userId]
                          ? profileMap[user.userId].partTimeFraction < 1.0
                            ? profileMap[user.userId].partTimeFraction.toFixed(2)
                            : '100%'
                          : '—'}
                      </td>
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

              {/* S53 TASK-5306e. Profile fields inlined from EmployeeProfileEditor. */}
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editPartTimeFraction">
                  Deltidsfraktion <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="editPartTimeFraction"
                  type="number"
                  required
                  min={0.1}
                  max={1.0}
                  step={0.001}
                  value={editForm.partTimeFraction}
                  onChange={(e) =>
                    setEditForm((f) => ({
                      ...f,
                      partTimeFraction: e.target.value,
                    }))
                  }
                  disabled={!editingProfile}
                  title={!editingProfile ? 'Profil ikke fundet for denne medarbejder' : undefined}
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editPosition">
                  Stilling
                </label>
                <input
                  className={styles.input}
                  id="editPosition"
                  type="text"
                  maxLength={100}
                  value={editForm.position}
                  onChange={(e) =>
                    setEditForm((f) => ({
                      ...f,
                      position: e.target.value,
                    }))
                  }
                  placeholder="f.eks. Fuldmaegtig"
                  disabled={!editingProfile}
                  title={!editingProfile ? 'Profil ikke fundet for denne medarbejder' : undefined}
                />
              </div>

              {/* S59 TASK-5908 (A). Foedselsdato — drives the age-derived
                  seniordag-berettigelse automatically (seniordage gives fra 62 aar).
                  HR-only field; DOB never leaves the Backend except as derived age. */}
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editBirthDate">
                  Fødselsdato
                </label>
                <input
                  className={styles.input}
                  id="editBirthDate"
                  type="date"
                  value={birthDate}
                  onChange={(e) => setBirthDateValue(e.target.value)}
                  disabled={birthDateVersion === null}
                  title={birthDateVersion === null ? 'Kunne ikke indlæse fødselsdato' : undefined}
                  data-testid="birth-date-input"
                />
                <div className={styles.helperText}>
                  Seniordage tildeles automatisk fra det fyldte 62. år ud fra fødselsdatoen.
                </div>
              </div>

              {/* S60 TASK-6007 (ADR-030). Ansættelsesdato — HR-only; pro-rates
                  optjent ferie for medarbejdere ansat midt i ferieåret. Mirrors
                  the DOB field (read-then-If-Match, admin-strict). */}
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editEmploymentStart">
                  Ansættelsesdato
                </label>
                <input
                  className={styles.input}
                  id="editEmploymentStart"
                  type="date"
                  value={employmentStartDate}
                  onChange={(e) => setEmploymentStartValue(e.target.value)}
                  disabled={employmentStartVersion === null}
                  title={employmentStartVersion === null ? 'Kunne ikke indlæse ansættelsesdato' : undefined}
                  data-testid="employment-start-input"
                />
                <div className={styles.helperText}>
                  Bruges til at beregne optjent ferie for medarbejdere ansat midt i ferieåret.
                </div>
              </div>

              {/* S59 TASK-5908 (B). Barns sygedag-berettigelse — per-employee
                  opt-in (default ikke berettiget). CHILD_SICK only; senior is
                  age-derived (no toggle). */}
              <div className={styles.formField}>
                <label className={styles.checkboxRow} htmlFor="editChildSick">
                  <input
                    id="editChildSick"
                    type="checkbox"
                    checked={childSickEligible}
                    onChange={(e) => {
                      setChildSickEligible(e.target.checked)
                      setChildSickDirty(true)
                    }}
                    data-testid="child-sick-toggle"
                  />
                  <span>Barns sygedag – berettiget</span>
                </label>
                <div className={styles.helperText}>
                  Giver medarbejderen adgang til at registrere barns 1./2./3. sygedag.
                </div>
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
            <div style={{ marginTop: 20, borderTop: '1px solid var(--color-border)', paddingTop: 16 }}>
              <div className={styles.formLabel} style={{ fontWeight: 600, fontSize: 14, marginBottom: 8 }}>
                Ledelseslinjer
              </div>
              {linesLoading && <div style={{ fontSize: 13, color: 'var(--color-text-secondary)' }}>Indlaeser...</div>}
              {!linesLoading && activeLines.length === 0 && (
                <div style={{ fontSize: 13, color: 'var(--color-text-secondary)' }}>Ingen ledelseslinjer registreret</div>
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
