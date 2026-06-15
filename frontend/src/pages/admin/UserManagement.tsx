// S76b / TASK-7604 — UserManagement reduced to the org-user LIST entry point.
//
// The create + edit DIALOGS (and the in-dialog reporting-line/manager section,
// DOB/CHILD_SICK/profile editing) were RETIRED into the unified
// `EditPersonDrawer` (the single create/edit-everything person surface). This
// page now KEEPS: the organisation selector, the user LIST table (incl. the
// "Deltid" column fed by `profileMap`), and `useOrgUsers`. "Opret bruger" opens
// the drawer in CREATE mode; a row-click opens it in EDIT mode for that `User`
// (the full row is in hand here). On a successful drawer save the list +
// profileMap refresh. There is now ONE save path (the drawer), reached from
// both the tree (MedarbejderAdministration) and this list (Codex c1-B4).
import { useState, useEffect } from 'react'
import styles from './UserManagement.module.css'
import { useOrganizations, useOrgUsers, type User } from '../../hooks/useAdmin'
import { Spinner } from '../../components/ui'
import { apiFetchWithEtag } from '../../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../../lib/etag'
import { EditPersonDrawer } from './EditPersonDrawer'

// S53 TASK-5306e. Employee profile snapshot — used here ONLY to render the
// "Deltid" column in the list (the editing of these fields moved to the drawer).
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

export function UserManagement() {
  const { organizations, loading: orgsLoading, error: orgsError } = useOrganizations()
  const [selectedOrgId, setSelectedOrgId] = useState('')
  const {
    users,
    loading: usersLoading,
    error: usersError,
    fetchUsers,
  } = useOrgUsers(selectedOrgId)

  // S76b/7604 — the unified EditPersonDrawer (create + edit) replaces the dialogs.
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [drawerUser, setDrawerUser] = useState<User | null>(null)

  // Per-user profile map for the "Deltid" column without N+1 GETs. Populated
  // lazily the first time a user list loads for the org; refreshed after a save.
  const [profileMap, setProfileMap] = useState<Record<string, EmployeeProfileSnapshot>>({})

  // Set default org once loaded.
  useEffect(() => {
    if (organizations.length > 0 && !selectedOrgId) {
      setSelectedOrgId(organizations[0].orgId)
    }
  }, [organizations, selectedOrgId])

  // S53 TASK-5306e. Fetch employee profiles for all loaded users so the table
  // "Deltid" column can render without N+1 GET-on-row-render. Failures are
  // silently swallowed (the column shows a dash). Resets when the org changes.
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
    setDrawerUser(null)
    setDrawerOpen(true)
  }

  // Row-click → open the drawer in EDIT mode. The full `User` (incl. version)
  // is already in hand from the list, so no extra fetch is needed — the drawer
  // composes its own If-Match from `user.version` + its own HR-field reads.
  const handleRowClick = (user: User) => {
    setDrawerUser(user)
    setDrawerOpen(true)
  }

  const handleDrawerClose = () => {
    setDrawerOpen(false)
    setDrawerUser(null)
  }

  // After any drawer mutation (create/edit), refresh the list + the profileMap.
  const handleDrawerSaved = async () => {
    await fetchUsers()
    // The profileMap refresh is driven by the `users` effect re-running after
    // fetchUsers; refresh it explicitly for the edited user so the Deltid column
    // reflects an in-place edit even when the user identity set is unchanged.
    if (drawerUser) {
      const p = await fetchEmployeeProfile(drawerUser.userId)
      if (p) setProfileMap((prev) => ({ ...prev, [drawerUser.userId]: p }))
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
            <button
              className={styles.createBtn}
              onClick={handleOpenCreate}
              data-testid="um-create"
            >
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
                        {user.email ?? '—'}
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

      {/* S76b/7604 — the unified create/edit drawer (replaces the retired
          create + edit dialogs). Reached from both this list and the tree. */}
      {drawerOpen && (
        <EditPersonDrawer
          open={drawerOpen}
          user={drawerUser}
          organizations={organizations}
          defaultOrgId={selectedOrgId}
          onClose={handleDrawerClose}
          onSaved={handleDrawerSaved}
        />
      )}
    </div>
  )
}
