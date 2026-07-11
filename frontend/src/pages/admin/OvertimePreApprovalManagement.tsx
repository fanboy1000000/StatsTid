import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../../lib/api'
import { useToast } from '../../components/ui/Toast'
import { Spinner } from '../../components/ui'
import styles from './OvertimePreApprovalManagement.module.css'

// S116 / TASK-11602 — THE PAGE REPAIR (the S116 L3 pre-existing defect): the
// list read used to call `GET /api/overtime/pre-approvals`, a route that did
// NOT exist (every load 404'd — the page never worked). TASK-11601 created it
// as a typed, scope-bounded admin list op; the read below now rides the typed
// form. The hand-written `PreApproval` interface (which GUESSED the shape —
// it invented a non-null `reason`, and omitted `approvedBy`/`approvedAt`) was
// deleted in favor of the GENERATED spec record (11 fields, incl. the genuinely
// non-null `employeeName` the admission join carries).
import type { components } from '../../lib/api-types'

type PreApproval =
  components['schemas']['StatsTid.Backend.Api.Contracts.OvertimePreApprovalAdminListItem']

function formatDate(dateStr: string): string {
  try {
    return new Date(dateStr).toLocaleDateString('da-DK')
  } catch {
    return dateStr
  }
}

function StatusBadge({ status }: { status: string }) {
  const label =
    status === 'PENDING' ? 'Afventer' :
    status === 'APPROVED' ? 'Godkendt' :
    status === 'REJECTED' ? 'Afvist' : status
  const className =
    status === 'PENDING' ? styles.badgePending :
    status === 'APPROVED' ? styles.badgeApproved :
    status === 'REJECTED' ? styles.badgeRejected : styles.badgePending
  return <span className={className}>{label}</span>
}

export function OvertimePreApprovalManagement() {
  const [preApprovals, setPreApprovals] = useState<PreApproval[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [actionLoading, setActionLoading] = useState<string | null>(null)
  const { toast } = useToast()

  const fetchPreApprovals = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/overtime/pre-approvals')
    if (result.ok) {
      setPreApprovals(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    fetchPreApprovals()
  }, [fetchPreApprovals])

  const handleApprove = async (id: string) => {
    setActionLoading(id)
    // S116 typed switch — no body either side (the optional reason body is not
    // sent today and the typed no-body form matches); response body discarded.
    const result = await apiClient.put('/api/overtime/pre-approval/{id}/approve', {
      params: { path: { id } },
    })
    setActionLoading(null)
    if (result.ok) {
      toast({ title: 'Godkendt', description: 'Forhåndsgodkendelse godkendt', variant: 'success' })
      await fetchPreApprovals()
    } else {
      setError(result.error)
    }
  }

  const handleReject = async (id: string) => {
    setActionLoading(id)
    // S116 typed switch — same no-body form; response body discarded.
    const result = await apiClient.put('/api/overtime/pre-approval/{id}/reject', {
      params: { path: { id } },
    })
    setActionLoading(null)
    if (result.ok) {
      toast({ title: 'Afvist', description: 'Forhåndsgodkendelse afvist', variant: 'success' })
      await fetchPreApprovals()
    } else {
      setError(result.error)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Overtidsgodkendelse</h1>
      </div>

      {error && <div className={styles.alert}>{error}</div>}

      {loading && <div className={styles.spinner}><Spinner size="lg" /></div>}

      {!loading && !error && preApprovals.length === 0 && (
        <div className={styles.emptyState}>Ingen ventende godkendelser</div>
      )}

      {!loading && preApprovals.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Medarbejder</th>
              <th>Periode</th>
              <th>Maks timer</th>
              <th>Begrundelse</th>
              <th>Status</th>
              <th>Oprettet</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {preApprovals.map((item) => {
              const isActioning = actionLoading === item.id
              return (
                <tr key={item.id}>
                  {/* S116 — `employeeName` is spec-non-null (the admission join
                      carries users.display_name), so the old `|| employeeId`
                      fallback guessed at a shape that cannot occur. */}
                  <td>{item.employeeName}</td>
                  <td>
                    {formatDate(item.periodStart)} &ndash; {formatDate(item.periodEnd)}
                  </td>
                  <td>{item.maxHours.toFixed(1)} t</td>
                  <td>{item.reason || '\u2014'}</td>
                  <td><StatusBadge status={item.status} /></td>
                  <td>{formatDate(item.createdAt)}</td>
                  <td>
                    {item.status === 'PENDING' ? (
                      <>
                        <button
                          className={styles.approveBtn}
                          onClick={() => handleApprove(item.id)}
                          disabled={isActioning}
                        >
                          {isActioning ? '...' : 'Godkend'}
                        </button>
                        <button
                          className={styles.rejectBtn}
                          onClick={() => handleReject(item.id)}
                          disabled={isActioning}
                        >
                          {isActioning ? '...' : 'Afvis'}
                        </button>
                      </>
                    ) : (
                      '\u2014'
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
