import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../../lib/api'
import styles from './OvertimePreApprovalManagement.module.css'

interface PreApproval {
  id: string
  employeeId: string
  employeeName: string
  periodStart: string
  periodEnd: string
  maxHours: number
  reason: string
  status: 'PENDING' | 'APPROVED' | 'REJECTED'
  createdAt: string
}

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

  const fetchPreApprovals = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<PreApproval[]>('/api/overtime/pre-approvals')
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
    const result = await apiClient.put<void>(`/api/overtime/pre-approval/${id}/approve`)
    setActionLoading(null)
    if (result.ok) {
      await fetchPreApprovals()
    } else {
      setError(result.error)
    }
  }

  const handleReject = async (id: string) => {
    setActionLoading(id)
    const result = await apiClient.put<void>(`/api/overtime/pre-approval/${id}/reject`)
    setActionLoading(null)
    if (result.ok) {
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

      {loading && <div className={styles.spinner}>Henter godkendelser...</div>}

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
                  <td>{item.employeeName || item.employeeId}</td>
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
