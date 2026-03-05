import { useState, type ChangeEvent } from 'react'
import { useOrganizations, useOrgUsers, useUserRoles } from '../../hooks/useAdmin'
import {
  Button,
  Select,
  Badge,
  Card,
  Alert,
  Spinner,
  Dialog,
  FormField,
  Input,
  Table,
} from '../../components/ui'
import type { RoleAssignment, Organization, User } from '../../types'
import styles from './RoleManagement.module.css'

const ROLE_OPTIONS = [
  { value: 'GLOBAL_ADMIN', label: 'Global administrator' },
  { value: 'LOCAL_ADMIN', label: 'Lokal administrator' },
  { value: 'LOCAL_HR', label: 'Lokal HR' },
  { value: 'LOCAL_LEADER', label: 'Lokal leder' },
  { value: 'EMPLOYEE', label: 'Medarbejder' },
]

const SCOPE_TYPE_OPTIONS = [
  { value: 'GLOBAL', label: 'Global' },
  { value: 'ORG_ONLY', label: 'Kun organisation' },
  { value: 'ORG_AND_DESCENDANTS', label: 'Organisation og underenheder' },
]

function roleDanishLabel(roleId: string): string {
  const found = ROLE_OPTIONS.find((r) => r.value === roleId)
  return found ? found.label : roleId
}

function scopeBadgeVariant(scopeType: string): 'info' | 'default' | 'warning' {
  switch (scopeType) {
    case 'GLOBAL':
      return 'info'
    case 'ORG_ONLY':
      return 'default'
    case 'ORG_AND_DESCENDANTS':
      return 'warning'
    default:
      return 'default'
  }
}

function scopeDanishLabel(scopeType: string): string {
  const found = SCOPE_TYPE_OPTIONS.find((s) => s.value === scopeType)
  return found ? found.label : scopeType
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleDateString('da-DK')
}

export function RoleManagement() {
  const { organizations } = useOrganizations()

  const [selectedOrgId, setSelectedOrgId] = useState('')
  const [selectedUserId, setSelectedUserId] = useState('')

  const { users } = useOrgUsers(selectedOrgId)
  const { roles, loading, error, grantRole, revokeRole } = useUserRoles(selectedUserId)

  // Grant role dialog state
  const [grantDialogOpen, setGrantDialogOpen] = useState(false)
  const [grantRoleId, setGrantRoleId] = useState('')
  const [grantOrgId, setGrantOrgId] = useState('')
  const [grantScopeType, setGrantScopeType] = useState('')
  const [grantExpiresAt, setGrantExpiresAt] = useState('')
  const [grantLoading, setGrantLoading] = useState(false)
  const [grantError, setGrantError] = useState<string | null>(null)

  // Revoke dialog state
  const [revokeDialogOpen, setRevokeDialogOpen] = useState(false)
  const [revokeTarget, setRevokeTarget] = useState<RoleAssignment | null>(null)
  const [revokeLoading, setRevokeLoading] = useState(false)
  const [revokeError, setRevokeError] = useState<string | null>(null)

  const orgOptions = organizations.map((org: Organization) => ({
    value: org.orgId,
    label: `${org.orgName} (${org.orgId})`,
  }))

  function handleSelectUser(userId: string) {
    setSelectedUserId(userId)
  }

  function openGrantDialog() {
    setGrantRoleId('')
    setGrantOrgId(selectedOrgId)
    setGrantScopeType('')
    setGrantExpiresAt('')
    setGrantError(null)
    setGrantDialogOpen(true)
  }

  async function handleGrantRole() {
    if (!grantRoleId || !grantScopeType) {
      setGrantError('Rolle og scope-type er paakraevet.')
      return
    }
    setGrantLoading(true)
    setGrantError(null)
    try {
      await grantRole({
        userId: selectedUserId,
        roleId: grantRoleId,
        orgId: grantOrgId || undefined,
        scopeType: grantScopeType,
        expiresAt: grantExpiresAt || undefined,
      })
      setGrantDialogOpen(false)
    } catch (err) {
      setGrantError(err instanceof Error ? err.message : String(err))
    } finally {
      setGrantLoading(false)
    }
  }

  function openRevokeDialog(assignment: RoleAssignment) {
    setRevokeTarget(assignment)
    setRevokeError(null)
    setRevokeDialogOpen(true)
  }

  async function handleRevoke() {
    if (!revokeTarget) return
    setRevokeLoading(true)
    setRevokeError(null)
    try {
      await revokeRole({
        userId: selectedUserId,
        assignmentId: revokeTarget.assignmentId,
      })
      setRevokeDialogOpen(false)
      setRevokeTarget(null)
    } catch (err) {
      setRevokeError(err instanceof Error ? err.message : String(err))
    } finally {
      setRevokeLoading(false)
    }
  }

  return (
    <div className={styles.page}>
      <h1 className={styles.title}>Roller</h1>

      {/* Step 1: Select organization */}
      <div className={styles.orgSelector}>
        <FormField label="Organisation" htmlFor="org-select" required>
          <Select
            id="org-select"
            options={orgOptions}
            value={selectedOrgId}
            onValueChange={(v: string) => {
              setSelectedOrgId(v)
              setSelectedUserId('')
            }}
            placeholder="Vaelg organisation..."
          />
        </FormField>
      </div>

      {/* Step 2: Show users in selected org */}
      {selectedOrgId && (
        <Card>
          <h2 className={styles.sectionTitle}>Brugere i organisation</h2>
          {users.length === 0 ? (
            <div className={styles.emptyState}>
              Ingen brugere fundet i denne organisation.
            </div>
          ) : (
            <Table
              headers={['Brugernavn', 'Navn', 'Bruger-ID']}
            >
              {users.map((user: User) => (
                <tr
                  key={user.userId}
                  className={`${styles.userRow} ${
                    selectedUserId === user.userId ? styles.userRowSelected : ''
                  }`}
                  onClick={() => handleSelectUser(user.userId)}
                >
                  <td>{user.username}</td>
                  <td>{user.displayName}</td>
                  <td>{user.userId}</td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      {/* Step 3: Show role assignments for selected user */}
      {selectedUserId && (
        <Card>
          <div className={styles.roleHeader}>
            <h2 className={styles.sectionTitle}>Rolletildelinger</h2>
            <Button variant="primary" size="sm" onClick={openGrantDialog}>
              Tildel rolle
            </Button>
          </div>

          {error && <Alert variant="error">{error}</Alert>}

          {loading ? (
            <div className={styles.loadingWrapper}>
              <Spinner />
            </div>
          ) : roles.length === 0 ? (
            <div className={styles.emptyState}>
              Ingen roller tildelt denne bruger.
            </div>
          ) : (
            <Table
              headers={[
                'Rolle',
                'Organisation',
                'Scope-type',
                'Tildelt af',
                'Tildelt dato',
                'Udloeber',
                'Handlinger',
              ]}
            >
              {roles.map((assignment: RoleAssignment) => (
                <tr key={assignment.assignmentId}>
                  <td>
                    <Badge>{roleDanishLabel(assignment.roleId)}</Badge>
                  </td>
                  <td>{assignment.orgId ?? 'Global'}</td>
                  <td>
                    <Badge variant={scopeBadgeVariant(assignment.scopeType)}>
                      {scopeDanishLabel(assignment.scopeType)}
                    </Badge>
                  </td>
                  <td>{assignment.assignedBy}</td>
                  <td>{formatDate(assignment.assignedAt)}</td>
                  <td>{formatDate(assignment.expiresAt)}</td>
                  <td>
                    <Button
                      variant="danger"
                      size="sm"
                      onClick={() => openRevokeDialog(assignment)}
                    >
                      Fjern
                    </Button>
                  </td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      {/* Grant role dialog */}
      <Dialog
        open={grantDialogOpen}
        onOpenChange={setGrantDialogOpen}
        title="Tildel rolle"
        description="Tildel en ny rolle til brugeren."
      >
        <div className={styles.dialogForm}>
          {grantError && <Alert variant="error">{grantError}</Alert>}

          <FormField label="Rolle" htmlFor="grant-role" required>
            <Select
              id="grant-role"
              options={ROLE_OPTIONS}
              value={grantRoleId}
              onValueChange={setGrantRoleId}
              placeholder="Vaelg rolle..."
            />
          </FormField>

          <FormField label="Organisation" htmlFor="grant-org">
            <Select
              id="grant-org"
              options={orgOptions}
              value={grantOrgId}
              onValueChange={setGrantOrgId}
              placeholder="Vaelg organisation..."
            />
          </FormField>

          <FormField label="Scope-type" htmlFor="grant-scope" required>
            <Select
              id="grant-scope"
              options={SCOPE_TYPE_OPTIONS}
              value={grantScopeType}
              onValueChange={setGrantScopeType}
              placeholder="Vaelg scope-type..."
            />
          </FormField>

          <FormField label="Udloeber" htmlFor="grant-expires">
            <Input
              id="grant-expires"
              type="date"
              value={grantExpiresAt}
              onChange={(e: ChangeEvent<HTMLInputElement>) => setGrantExpiresAt(e.target.value)}
            />
          </FormField>

          <div className={styles.dialogActions}>
            <Button
              variant="secondary"
              onClick={() => setGrantDialogOpen(false)}
              disabled={grantLoading}
            >
              Annuller
            </Button>
            <Button
              variant="primary"
              onClick={handleGrantRole}
              disabled={grantLoading}
            >
              {grantLoading ? 'Tildeler...' : 'Tildel'}
            </Button>
          </div>
        </div>
      </Dialog>

      {/* Revoke confirmation dialog */}
      <Dialog
        open={revokeDialogOpen}
        onOpenChange={setRevokeDialogOpen}
        title="Fjern rolle"
        description="Er du sikker paa, at du vil fjerne denne rolle?"
      >
        <div className={styles.dialogForm}>
          {revokeError && <Alert variant="error">{revokeError}</Alert>}

          {revokeTarget && (
            <p className={styles.confirmText}>
              Du er ved at fjerne rollen{' '}
              <strong>{roleDanishLabel(revokeTarget.roleId)}</strong> (
              {scopeDanishLabel(revokeTarget.scopeType)}) fra brugeren.
            </p>
          )}

          <div className={styles.dialogActions}>
            <Button
              variant="secondary"
              onClick={() => setRevokeDialogOpen(false)}
              disabled={revokeLoading}
            >
              Annuller
            </Button>
            <Button
              variant="danger"
              onClick={handleRevoke}
              disabled={revokeLoading}
            >
              {revokeLoading ? 'Fjerner...' : 'Fjern rolle'}
            </Button>
          </div>
        </div>
      </Dialog>
    </div>
  )
}
