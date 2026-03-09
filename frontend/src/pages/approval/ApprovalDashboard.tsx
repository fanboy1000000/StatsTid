import { useState, useEffect, useCallback } from 'react'
import styles from './ApprovalDashboard.module.css'

const TOKEN_KEY = 'statstid_token'

interface ApprovalPeriod {
  periodId: string
  employeeId: string
  orgId: string
  periodStart: string
  periodEnd: string
  periodType: string
  status: string
  agreementCode: string
  okVersion: string
  submittedAt: string | null
  approvedBy: string | null
  approvedAt: string | null
  rejectionReason: string | null
  createdAt: string
}

function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function handle401(res: Response) {
  if (res.status === 401) {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem('statstid_user')
    window.location.reload()
  }
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '-'
  try {
    return new Date(dateStr).toLocaleDateString('da-DK')
  } catch {
    return dateStr
  }
}

const PERIOD_TYPE_LABELS: Record<string, string> = {
  WEEKLY: 'Ugentlig',
  MONTHLY: 'Maanedlig',
}

export function ApprovalDashboard() {
  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null)

  // Rejection dialog state
  const [rejectTarget, setRejectTarget] = useState<ApprovalPeriod | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)

  // Track which period is being approved
  const [approvingId, setApprovingId] = useState<string | null>(null)

  const showToast = useCallback((message: string, variant: 'success' | 'error') => {
    setToast({ message, variant })
    setTimeout(() => setToast(null), 4000)
  }, [])

  const fetchPending = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const token = getToken()
      const res = await fetch(`/api/approval/pending`, {
        headers: token ? { 'Authorization': `Bearer ${token}` } : {},
      })
      if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
      const data: ApprovalPeriod[] = await res.json()
      setPeriods(data)
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchPending() }, [fetchPending])

  const handleApprove = async (periodId: string) => {
    setApprovingId(periodId)
    try {
      const token = getToken()
      const res = await fetch(`/api/approval/${periodId}/approve`, {
        method: 'POST',
        headers: {
          ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
        },
      })
      if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
      showToast('Periode godkendt.', 'success')
      await fetchPending()
    } catch (e) {
      showToast(String(e instanceof Error ? e.message : e), 'error')
    } finally {
      setApprovingId(null)
    }
  }

  const openRejectDialog = (period: ApprovalPeriod) => {
    setRejectTarget(period)
    setRejectReason('')
  }

  const closeRejectDialog = () => {
    setRejectTarget(null)
    setRejectReason('')
    setRejecting(false)
  }

  const handleReject = async () => {
    if (!rejectTarget) return
    setRejecting(true)
    try {
      const token = getToken()
      const res = await fetch(`/api/approval/${rejectTarget.periodId}/reject`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { 'Authorization': `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({ reason: rejectReason }),
      })
      if (!res.ok) { handle401(res); throw new Error(`HTTP ${res.status}`) }
      showToast('Periode afvist.', 'success')
      closeRejectDialog()
      await fetchPending()
    } catch (e) {
      showToast(String(e instanceof Error ? e.message : e), 'error')
      setRejecting(false)
    }
  }

  return (
    <div className={styles.page}>
      <h2 className={styles.pageTitle}>Godkendelser</h2>

      {toast && (
        <div className={toast.variant === 'success' ? styles.alertSuccess : styles.alert}>
          {toast.message}
        </div>
      )}

      <div className={styles.card}>
        <div className={styles.cardHeaderRow}>
          <h3 className={styles.cardHeaderTitle}>Ventende perioder</h3>
          <span className={styles.countBadge}>{periods.length}</span>
        </div>

        {loading && (
          <div className={styles.spinner}>Henter ventende perioder...</div>
        )}

        {error && (
          <div className={styles.alert}>{error}</div>
        )}

        {!loading && !error && periods.length === 0 && (
          <p className={styles.emptyState}>Ingen ventende perioder.</p>
        )}

        {periods.length > 0 && (
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Medarbejder</th>
                <th>Organisation</th>
                <th>Periode</th>
                <th>Type</th>
                <th>Overenskomst</th>
                <th>Indsendt</th>
                <th>Handlinger</th>
              </tr>
            </thead>
            <tbody>
              {periods.map(p => (
                <tr key={p.periodId}>
                  <td>{p.employeeId}</td>
                  <td>{p.orgId}</td>
                  <td>{formatDate(p.periodStart)} &ndash; {formatDate(p.periodEnd)}</td>
                  <td>{PERIOD_TYPE_LABELS[p.periodType] ?? p.periodType}</td>
                  <td>{p.agreementCode}</td>
                  <td>{formatDate(p.submittedAt)}</td>
                  <td>
                    <div className={styles.actionCell}>
                      <button
                        className={styles.approveButton}
                        onClick={() => handleApprove(p.periodId)}
                        disabled={approvingId === p.periodId}
                      >
                        {approvingId === p.periodId ? 'Godkender...' : 'Godkend'}
                      </button>
                      <button
                        className={styles.rejectButton}
                        onClick={() => openRejectDialog(p)}
                      >
                        Afvis
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Rejection dialog */}
      {rejectTarget && (
        <div className={styles.dialogOverlay} onClick={closeRejectDialog}>
          <div className={styles.dialog} onClick={e => e.stopPropagation()}>
            <h3 className={styles.dialogTitle}>Afvis periode</h3>
            <p className={styles.dialogDescription}>
              Angiv begrundelse for afvisning af perioden for {rejectTarget.employeeId} ({formatDate(rejectTarget.periodStart)} &ndash; {formatDate(rejectTarget.periodEnd)}).
            </p>
            <textarea
              className={styles.dialogTextarea}
              value={rejectReason}
              onChange={e => setRejectReason(e.target.value)}
              placeholder="Begrundelse for afvisning..."
              autoFocus
            />
            <div className={styles.dialogActions}>
              <button
                className={styles.cancelButton}
                onClick={closeRejectDialog}
                type="button"
              >
                Annuller
              </button>
              <button
                className={styles.confirmRejectButton}
                onClick={handleReject}
                disabled={rejecting || !rejectReason.trim()}
                type="button"
              >
                {rejecting ? 'Afviser...' : 'Afvis periode'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
