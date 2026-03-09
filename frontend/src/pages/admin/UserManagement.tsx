import { useState, useEffect, useCallback, type FormEvent } from 'react'
import styles from './UserManagement.module.css'

const TOKEN_KEY = 'statstid_token'

const AGREEMENT_CODES = ['AC', 'HK', 'PROSA'] as const

interface Organization {
  orgId: string
  orgName: string
  orgType: string
  parentOrgId: string | null
  materializedPath: string
  agreementCode: string
}

interface User {
  userId: string
  username: string
  displayName: string
  email: string | null
  primaryOrgId: string
  agreementCode: string
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
}

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  return token
    ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }
    : { 'Content-Type': 'application/json' }
}

function useOrganizations() {
  const [organizations, setOrganizations] = useState<Organization[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const token = getToken()
        const res = await fetch(`/api/admin/organizations`, {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        })
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const data: Organization[] = await res.json()
        if (!cancelled) setOrganizations(data)
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : String(err))
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    void load()
    return () => { cancelled = true }
  }, [])

  return { organizations, loading, error }
}

function useOrgUsers(orgId: string) {
  const [users, setUsers] = useState<User[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchUsers = useCallback(async (targetOrgId: string) => {
    if (!targetOrgId) return
    setLoading(true)
    setError(null)
    try {
      const res = await fetch(
        `/api/admin/organizations/${encodeURIComponent(targetOrgId)}/users`,
        { headers: authHeaders() }
      )
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data: User[] = await res.json()
      setUsers(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void fetchUsers(orgId)
  }, [orgId, fetchUsers])

  const createUser = useCallback(
    async (userData: {
      userId: string
      username: string
      password: string
      displayName: string
      email?: string
      primaryOrgId: string
      agreementCode: string
    }) => {
      const res = await fetch(`/api/admin/users`, {
        method: 'POST',
        headers: authHeaders(),
        body: JSON.stringify(userData),
      })
      if (!res.ok) {
        const text = await res.text().catch(() => '')
        throw new Error(text || `HTTP ${res.status}`)
      }
      await fetchUsers(orgId)
    },
    [orgId, fetchUsers]
  )

  const updateUser = useCallback(
    async (
      userId: string,
      updates: {
        displayName?: string
        email?: string
        primaryOrgId?: string
        agreementCode?: string
      }
    ) => {
      const res = await fetch(
        `/api/admin/users/${encodeURIComponent(userId)}`,
        {
          method: 'PUT',
          headers: authHeaders(),
          body: JSON.stringify(updates),
        }
      )
      if (!res.ok) {
        const text = await res.text().catch(() => '')
        throw new Error(text || `HTTP ${res.status}`)
      }
      await fetchUsers(orgId)
    },
    [orgId, fetchUsers]
  )

  return { users, loading, error, createUser, updateUser }
}

export function UserManagement() {
  const { organizations, loading: orgsLoading, error: orgsError } = useOrganizations()
  const [selectedOrgId, setSelectedOrgId] = useState('')
  const { users, loading: usersLoading, error: usersError, createUser, updateUser } =
    useOrgUsers(selectedOrgId)

  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [editingUser, setEditingUser] = useState<User | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

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
      handleCloseCreate()
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleRowClick = (user: User) => {
    setEditingUser(user)
    setEditForm({
      displayName: user.displayName,
      email: user.email ?? '',
      primaryOrgId: user.primaryOrgId,
      agreementCode: user.agreementCode,
    })
    setFormError(null)
    setEditDialogOpen(true)
  }

  const handleCloseEdit = () => {
    setEditDialogOpen(false)
    setEditingUser(null)
    setFormError(null)
  }

  const handleEditSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!editingUser) return
    setSubmitting(true)
    setFormError(null)
    try {
      await updateUser(editingUser.userId, {
        displayName: editForm.displayName,
        email: editForm.email || undefined,
        primaryOrgId: editForm.primaryOrgId,
        agreementCode: editForm.agreementCode,
      })
      handleCloseEdit()
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
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
          <div className={styles.spinner}>Henter organisationer...</div>
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
              <div className={styles.spinner}>Henter brugere...</div>
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
          </div>
        </div>
      )}
    </div>
  )
}
