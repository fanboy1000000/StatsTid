import { useState, useEffect, useCallback } from 'react'
import { usePendingApprovals, usePendingMyReports } from '../../hooks/useApprovals'
import { apiClient } from '../../lib/api'
import { Tabs } from '../../components/ui/Tabs'
import type { ApprovalPeriod } from '../../types'
import type { ComplianceCheckResult } from '../../hooks/useCompliance'
import styles from './ApprovalDashboard.module.css'

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

interface PendingTableProps {
  periods: ApprovalPeriod[]
  loading: boolean
  error: string | null
  complianceMap: Record<string, ComplianceCheckResult>
  approvingId: string | null
  onApprove: (periodId: string) => void
  onReject: (period: ApprovalPeriod) => void
}

function PendingTable({
  periods,
  loading,
  error,
  complianceMap,
  approvingId,
  onApprove,
  onReject,
}: PendingTableProps) {
  if (loading) {
    return <div className={styles.spinner}>Henter ventende perioder...</div>
  }

  if (error) {
    return <div className={styles.alert}>{error}</div>
  }

  if (periods.length === 0) {
    return <p className={styles.emptyState}>Ingen ventende perioder.</p>
  }

  return (
    <table className={styles.table}>
      <thead>
        <tr>
          <th>Medarbejder</th>
          <th>Organisation</th>
          <th>Periode</th>
          <th>Type</th>
          <th>Overenskomst</th>
          <th>Indsendt</th>
          <th>Compliance</th>
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
              <ComplianceBadge result={complianceMap[p.periodId] ?? null} />
            </td>
            <td>
              <div className={styles.actionCell}>
                <button
                  className={styles.approveButton}
                  onClick={() => onApprove(p.periodId)}
                  disabled={approvingId === p.periodId}
                >
                  {approvingId === p.periodId ? 'Godkender...' : 'Godkend'}
                </button>
                <button
                  className={styles.rejectButton}
                  onClick={() => onReject(p)}
                >
                  Afvis
                </button>
              </div>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

export function ApprovalDashboard() {
  const {
    periods: allPeriods,
    loading: allLoading,
    error: allError,
    fetchPending: fetchAll,
  } = usePendingApprovals()

  const {
    periods: myReportPeriods,
    loading: myReportsLoading,
    error: myReportsError,
    fetchPendingMyReports,
  } = usePendingMyReports()

  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null)

  // Rejection dialog state
  const [rejectTarget, setRejectTarget] = useState<ApprovalPeriod | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)

  // Track which period is being approved
  const [approvingId, setApprovingId] = useState<string | null>(null)

  // Enforcement confirmation dialog state (428 response)
  const [enforcementDialog, setEnforcementDialog] = useState<{
    periodId: string
    action: 'approve' | 'reject'
    designatedApproverId: string | null
    reason?: string
  } | null>(null)
  const [enforcementConfirming, setEnforcementConfirming] = useState(false)

  // Compliance results per period — shared across both tabs
  const [complianceMap, setComplianceMap] = useState<Record<string, ComplianceCheckResult>>({})

  const showToast = useCallback((message: string, variant: 'success' | 'error') => {
    setToast({ message, variant })
    setTimeout(() => setToast(null), 4000)
  }, [])

  // Merge periods from both sources for compliance fetching (deduplicate by periodId)
  const allUniquePeriods = [...allPeriods, ...myReportPeriods].reduce<ApprovalPeriod[]>(
    (acc, p) => {
      if (!acc.some(existing => existing.periodId === p.periodId)) {
        acc.push(p)
      }
      return acc
    },
    [],
  )

  // Fetch compliance for each unique pending period
  useEffect(() => {
    async function fetchCompliance() {
      const results: Record<string, ComplianceCheckResult> = {}
      for (const p of allUniquePeriods) {
        try {
          const start = new Date(p.periodStart)
          const result = await apiClient.get<ComplianceCheckResult>(
            `/api/compliance/${p.employeeId}/period?year=${start.getFullYear()}&month=${start.getMonth() + 1}`,
          )
          if (result.ok) {
            results[p.periodId] = result.data
          }
        } catch { /* ignore */ }
      }
      setComplianceMap(results)
    }
    if (allUniquePeriods.length > 0) fetchCompliance()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [allPeriods, myReportPeriods])

  const refreshAll = useCallback(async () => {
    await Promise.all([fetchAll(), fetchPendingMyReports()])
  }, [fetchAll, fetchPendingMyReports])

  const handleApprove = async (periodId: string, confirmFallback?: boolean) => {
    setApprovingId(periodId)
    try {
      const url = confirmFallback
        ? `/api/approval/${periodId}/approve?confirmFallback=true`
        : `/api/approval/${periodId}/approve`
      const result = await apiClient.post<unknown>(url)
      if (!result.ok) {
        if (result.status === 428) {
          let body: { designatedApproverId?: string | null } = {}
          try { body = JSON.parse(result.error) as typeof body } catch { /* ignore */ }
          setEnforcementDialog({
            periodId,
            action: 'approve',
            designatedApproverId: body.designatedApproverId ?? null,
          })
          return
        }
        throw new Error(result.error)
      }
      showToast('Periode godkendt.', 'success')
      await refreshAll()
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

  const handleReject = async (confirmFallback?: boolean) => {
    if (!rejectTarget) return
    setRejecting(true)
    try {
      const url = confirmFallback
        ? `/api/approval/${rejectTarget.periodId}/reject?confirmFallback=true`
        : `/api/approval/${rejectTarget.periodId}/reject`
      const result = await apiClient.post<unknown>(url, { reason: rejectReason })
      if (!result.ok) {
        if (result.status === 428) {
          let body: { designatedApproverId?: string | null } = {}
          try { body = JSON.parse(result.error) as typeof body } catch { /* ignore */ }
          setEnforcementDialog({
            periodId: rejectTarget.periodId,
            action: 'reject',
            designatedApproverId: body.designatedApproverId ?? null,
            reason: rejectReason,
          })
          closeRejectDialog()
          return
        }
        throw new Error(result.error)
      }
      showToast('Periode afvist.', 'success')
      closeRejectDialog()
      await refreshAll()
    } catch (e) {
      showToast(String(e instanceof Error ? e.message : e), 'error')
      setRejecting(false)
    }
  }

  const closeEnforcementDialog = () => {
    setEnforcementDialog(null)
    setEnforcementConfirming(false)
  }

  const handleEnforcementConfirm = async () => {
    if (!enforcementDialog) return
    setEnforcementConfirming(true)
    try {
      if (enforcementDialog.action === 'approve') {
        await handleApprove(enforcementDialog.periodId, true)
      } else {
        // For reject, call the reject endpoint directly with confirmFallback
        const result = await apiClient.post<unknown>(
          `/api/approval/${enforcementDialog.periodId}/reject?confirmFallback=true`,
          { reason: enforcementDialog.reason },
        )
        if (!result.ok) throw new Error(result.error)
        showToast('Periode afvist.', 'success')
        await refreshAll()
      }
      closeEnforcementDialog()
    } catch (e) {
      showToast(String(e instanceof Error ? e.message : e), 'error')
      setEnforcementConfirming(false)
    }
  }

  const tabs = [
    {
      value: 'my-reports',
      label: `Mine medarbejdere (${myReportPeriods.length})`,
      content: (
        <PendingTable
          periods={myReportPeriods}
          loading={myReportsLoading}
          error={myReportsError}
          complianceMap={complianceMap}
          approvingId={approvingId}
          onApprove={handleApprove}
          onReject={openRejectDialog}
        />
      ),
    },
    {
      value: 'all',
      label: `Alle i omraade (${allPeriods.length})`,
      content: (
        <PendingTable
          periods={allPeriods}
          loading={allLoading}
          error={allError}
          complianceMap={complianceMap}
          approvingId={approvingId}
          onApprove={handleApprove}
          onReject={openRejectDialog}
        />
      ),
    },
  ]

  return (
    <div className={styles.page}>
      <h2 className={styles.pageTitle}>Godkendelser</h2>

      {toast && (
        <div className={toast.variant === 'success' ? styles.alertSuccess : styles.alert}>
          {toast.message}
        </div>
      )}

      <Tabs tabs={tabs} defaultValue="my-reports" />

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
                onClick={() => handleReject()}
                disabled={rejecting || !rejectReason.trim()}
                type="button"
              >
                {rejecting ? 'Afviser...' : 'Afvis periode'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Enforcement confirmation dialog (428 response) */}
      {enforcementDialog && (
        <div className={styles.dialogOverlay} onClick={closeEnforcementDialog}>
          <div className={styles.dialog} onClick={e => e.stopPropagation()}>
            <h3 className={styles.dialogTitle}>Haandhaevelse aktiv</h3>
            <p className={styles.dialogDescription}>
              Godkendelse kraever den udpegede leder. Du er ikke den udpegede leder for denne medarbejder.
              Vil du {enforcementDialog.action === 'approve' ? 'godkende' : 'afvise'} alligevel med organisationskopet?
            </p>
            {enforcementDialog.designatedApproverId && (
              <p className={styles.dialogDescription}>
                Udpeget leder: {enforcementDialog.designatedApproverId}
              </p>
            )}
            <div className={styles.dialogActions}>
              <button
                className={styles.cancelButton}
                onClick={closeEnforcementDialog}
                type="button"
              >
                Annuller
              </button>
              <button
                className={styles.approveButton}
                onClick={handleEnforcementConfirm}
                disabled={enforcementConfirming}
                type="button"
              >
                {enforcementConfirming
                  ? (enforcementDialog.action === 'approve' ? 'Godkender...' : 'Afviser...')
                  : (enforcementDialog.action === 'approve' ? 'Godkend alligevel' : 'Afvis alligevel')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function ComplianceBadge({ result }: { result: ComplianceCheckResult | null }) {
  if (!result) {
    return <span className={styles.complianceLoading}>...</span>
  }
  if (result.violations.length > 0) {
    return (
      <span className={styles.complianceViolation} title={`${result.violations.length} overtraedelse(r)`}>
        {result.violations.length} overtr.
      </span>
    )
  }
  if (result.warnings.length > 0) {
    return (
      <span className={styles.complianceWarning} title={`${result.warnings.length} advarsel(er)`}>
        {result.warnings.length} adv.
      </span>
    )
  }
  return <span className={styles.complianceOk}>OK</span>
}
