import { useState, useEffect, useCallback, type FormEvent } from 'react'
import styles from './OrgManagement.module.css'

const TOKEN_KEY = 'statstid_token'

const AGREEMENT_CODES = ['AC', 'HK', 'PROSA'] as const

const ORG_TYPE_OPTIONS = [
  { value: 'MINISTRY', label: 'Ministerium' },
  { value: 'STYRELSE', label: 'Styrelse' },
  { value: 'AFDELING', label: 'Afdeling' },
  { value: 'TEAM', label: 'Team' },
] as const

const ORG_TYPE_LABELS: Record<string, string> = {
  MINISTRY: 'Ministerium',
  STYRELSE: 'Styrelse',
  AFDELING: 'Afdeling',
  TEAM: 'Team',
}

interface Organization {
  orgId: string
  orgName: string
  orgType: string
  parentOrgId: string | null
  materializedPath: string
  agreementCode: string
  okVersion?: string
}

interface CreateOrgForm {
  orgId: string
  orgName: string
  orgType: string
  parentOrgId: string
  agreementCode: string
}

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function getPathDepth(path: string): number {
  if (!path) return 0
  const parts = path.split('/').filter(Boolean)
  return Math.max(0, parts.length - 1)
}

function useOrganizations() {
  const [organizations, setOrganizations] = useState<Organization[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchOrganizations = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const token = getToken()
      const res = await fetch(`/api/admin/organizations`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data: Organization[] = await res.json()
      setOrganizations(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void fetchOrganizations()
  }, [fetchOrganizations])

  const createOrganization = useCallback(
    async (org: {
      orgId: string
      orgName: string
      orgType: string
      parentOrgId: string | null
      agreementCode: string
    }) => {
      const token = getToken()
      const res = await fetch(`/api/admin/organizations`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify(org),
      })
      if (!res.ok) {
        const text = await res.text().catch(() => '')
        throw new Error(text || `HTTP ${res.status}`)
      }
      await fetchOrganizations()
    },
    [fetchOrganizations]
  )

  const updateOrganization = useCallback(
    async (
      orgId: string,
      updates: { orgName: string; agreementCode: string; okVersion: string }
    ) => {
      const token = getToken()
      const res = await fetch(`/api/admin/organizations/${orgId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify(updates),
      })
      if (!res.ok) {
        const text = await res.text().catch(() => '')
        throw new Error(text || `HTTP ${res.status}`)
      }
      await fetchOrganizations()
    },
    [fetchOrganizations]
  )

  return { organizations, loading, error, createOrganization, updateOrganization, refetch: fetchOrganizations }
}

export function OrgManagement() {
  const { organizations, loading, error, createOrganization, updateOrganization } = useOrganizations()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [form, setForm] = useState<CreateOrgForm>({
    orgId: '',
    orgName: '',
    orgType: 'STYRELSE',
    parentOrgId: '',
    agreementCode: 'AC',
  })

  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [editOrgId, setEditOrgId] = useState<string>('')
  const [editForm, setEditForm] = useState<{
    orgId: string
    orgName: string
    orgType: string
    parentOrgId: string | null
    agreementCode: string
    okVersion: string
  }>({ orgId: '', orgName: '', orgType: '', parentOrgId: null, agreementCode: 'AC', okVersion: 'OK24' })
  const [editSubmitting, setEditSubmitting] = useState(false)
  const [editFormError, setEditFormError] = useState<string | null>(null)

  const sortedOrgs = [...organizations].sort((a, b) =>
    a.materializedPath.localeCompare(b.materializedPath)
  )

  const resetForm = () => {
    setForm({
      orgId: '',
      orgName: '',
      orgType: 'STYRELSE',
      parentOrgId: '',
      agreementCode: 'AC',
    })
    setFormError(null)
  }

  const handleOpen = () => {
    resetForm()
    setDialogOpen(true)
  }

  const handleClose = () => {
    setDialogOpen(false)
    resetForm()
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setFormError(null)
    try {
      await createOrganization({
        orgId: form.orgId,
        orgName: form.orgName,
        orgType: form.orgType,
        parentOrgId: form.parentOrgId || null,
        agreementCode: form.agreementCode,
      })
      handleClose()
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleEditOpen = (org: Organization) => {
    setEditOrgId(org.orgId)
    setEditForm({
      orgId: org.orgId,
      orgName: org.orgName,
      orgType: org.orgType,
      parentOrgId: org.parentOrgId,
      agreementCode: org.agreementCode,
      okVersion: org.okVersion ?? 'OK24',
    })
    setEditFormError(null)
    setEditDialogOpen(true)
  }

  const handleEditClose = () => {
    setEditDialogOpen(false)
    setEditFormError(null)
  }

  const handleEditSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setEditSubmitting(true)
    setEditFormError(null)
    try {
      await updateOrganization(editOrgId, {
        orgName: editForm.orgName,
        agreementCode: editForm.agreementCode,
        okVersion: editForm.okVersion,
      })
      handleEditClose()
    } catch (err) {
      setEditFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setEditSubmitting(false)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Organisation</h1>
        <button className={styles.createBtn} onClick={handleOpen}>
          Opret organisation
        </button>
      </div>

      {error && <div className={styles.alert}>{error}</div>}

      {loading && (
        <div className={styles.spinner}>Henter organisationer...</div>
      )}

      {!loading && !error && sortedOrgs.length === 0 && (
        <div className={styles.emptyState}>Ingen organisationer fundet</div>
      )}

      {!loading && sortedOrgs.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>ID</th>
              <th>Navn</th>
              <th>Type</th>
              <th>Overordnet</th>
              <th>Overenskomst</th>
              <th>Sti</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {sortedOrgs.map((org) => {
              const depth = getPathDepth(org.materializedPath)
              return (
                <tr key={org.orgId}>
                  <td>{org.orgId}</td>
                  <td>
                    <span
                      className={styles.indentedName}
                      style={{ marginLeft: depth * 20 }}
                    >
                      {org.orgName}
                    </span>
                  </td>
                  <td>
                    <span className={styles.badge}>
                      {ORG_TYPE_LABELS[org.orgType] ?? org.orgType}
                    </span>
                  </td>
                  <td>{org.parentOrgId ?? '\u2014'}</td>
                  <td>{org.agreementCode}</td>
                  <td className={styles.pathCell}>{org.materializedPath}</td>
                  <td>
                    <button
                      className={styles.createBtn}
                      onClick={() => handleEditOpen(org)}
                    >
                      Rediger
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      {dialogOpen && (
        <div className={styles.overlay} onClick={handleClose}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Opret organisation</h2>
            <form onSubmit={handleSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="orgId">
                  Organisations-ID <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="orgId"
                  type="text"
                  required
                  value={form.orgId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, orgId: e.target.value }))
                  }
                  placeholder="f.eks. STY01"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="orgName">
                  Navn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="orgName"
                  type="text"
                  required
                  value={form.orgName}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, orgName: e.target.value }))
                  }
                  placeholder="Organisationsnavn"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="orgType">
                  Type <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="orgType"
                  value={form.orgType}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, orgType: e.target.value }))
                  }
                >
                  {ORG_TYPE_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="parentOrgId">
                  Overordnet org
                </label>
                <select
                  className={styles.select}
                  id="parentOrgId"
                  value={form.parentOrgId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, parentOrgId: e.target.value }))
                  }
                >
                  <option value="">(Ingen - topniveau)</option>
                  {organizations.map((org) => (
                    <option key={org.orgId} value={org.orgId}>
                      {org.orgName} ({org.orgId})
                    </option>
                  ))}
                </select>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="agreementCode">
                  Overenskomst <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="agreementCode"
                  value={form.agreementCode}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, agreementCode: e.target.value }))
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
                  onClick={handleClose}
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

      {editDialogOpen && (
        <div className={styles.overlay} onClick={handleEditClose}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Rediger organisation</h2>
            <form onSubmit={handleEditSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel}>Organisations-ID</label>
                <span>{editForm.orgId}</span>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel}>Type</label>
                <span>{ORG_TYPE_LABELS[editForm.orgType] ?? editForm.orgType}</span>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel}>Overordnet org</label>
                <span>{editForm.parentOrgId ?? '\u2014'}</span>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editOrgName">
                  Navn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="editOrgName"
                  type="text"
                  required
                  value={editForm.orgName}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, orgName: e.target.value }))
                  }
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editAgreementCode">
                  Overenskomst <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="editAgreementCode"
                  value={editForm.agreementCode}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, agreementCode: e.target.value }))
                  }
                >
                  {AGREEMENT_CODES.map((code) => (
                    <option key={code} value={code}>
                      {code}
                    </option>
                  ))}
                </select>
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="editOkVersion">
                  OK Version <span className={styles.required}>*</span>
                </label>
                <select
                  className={styles.select}
                  id="editOkVersion"
                  value={editForm.okVersion}
                  onChange={(e) =>
                    setEditForm((f) => ({ ...f, okVersion: e.target.value }))
                  }
                >
                  {['OK24', 'OK26'].map((v) => (
                    <option key={v} value={v}>
                      {v}
                    </option>
                  ))}
                </select>
              </div>

              {editFormError && <div className={styles.alert}>{editFormError}</div>}

              <div className={styles.dialogActions}>
                <button
                  type="button"
                  className={styles.cancelBtn}
                  onClick={handleEditClose}
                >
                  Annuller
                </button>
                <button
                  type="submit"
                  className={styles.createBtn}
                  disabled={editSubmitting}
                >
                  {editSubmitting ? 'Gemmer...' : 'Gem'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
