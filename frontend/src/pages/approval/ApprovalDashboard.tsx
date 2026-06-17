import React, { useState, useCallback } from 'react'
import { useApprovalsByMonth, useMyReportsByMonth } from '../../hooks/useApprovals'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import { formatMonthLabel } from '../../lib/locale'
import { apiClient } from '../../lib/api'
import { Tabs } from '../../components/ui/Tabs'
import { Button } from '../../components/ui/Button'
import { Badge } from '../../components/ui/Badge'
import { Dialog } from '../../components/ui/Dialog'
import { ApprovalDetailPanel } from './ApprovalDetailPanel'
import type { ApprovalPeriod } from '../../types'
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

interface PeriodsTableProps {
  periods: ApprovalPeriod[]
  loading: boolean
  error: string | null
  approvingId: string | null
  reopeningId: string | null
  userRole: string | null
  onApprove: (periodId: string) => void
  onReject: (period: ApprovalPeriod) => void
  onReopen: (periodId: string) => void
}

function PeriodsTable({
  periods,
  loading,
  error,
  approvingId,
  reopeningId,
  userRole,
  onApprove,
  onReject,
  onReopen,
}: PeriodsTableProps) {
  const STATUS_LABELS: Record<string, string> = {
    DRAFT: 'Kladde',
    SUBMITTED: 'Indsendt',
    EMPLOYEE_APPROVED: 'Medarb. godkendt',
    APPROVED: 'Godkendt',
    REJECTED: 'Afvist',
  }

  const STATUS_VARIANTS: Record<string, 'default' | 'success' | 'error' | 'warning' | 'info'> = {
    DRAFT: 'default',
    SUBMITTED: 'info',
    EMPLOYEE_APPROVED: 'info',
    APPROVED: 'success',
    REJECTED: 'error',
  }

  const [expandedPeriodId, setExpandedPeriodId] = useState<string | null>(null)
  const toggleExpand = (periodId: string) =>
    setExpandedPeriodId(prev => (prev === periodId ? null : periodId))

  if (loading) {
    return <div className={styles.spinner}>Henter perioder...</div>
  }

  if (error) {
    return <div className={styles.alert}>{error}</div>
  }

  if (periods.length === 0) {
    return <p className={styles.emptyState}>Ingen perioder for denne maaned.</p>
  }

  return (
    <table className={styles.table}>
      <thead>
        <tr>
          <th className={styles.expandCell}></th>
          <th>Medarbejder</th>
          <th>Organisation</th>
          <th>Periode</th>
          <th>Type</th>
          <th>Overenskomst</th>
          <th>Status</th>
          <th>Handlinger</th>
        </tr>
      </thead>
      <tbody>
        {periods.map(p => (
          <React.Fragment key={p.periodId}>
            <tr
              className={`${styles.periodRow}${expandedPeriodId === p.periodId ? ` ${styles.periodRowExpanded}` : ''}`}
              onClick={() => toggleExpand(p.periodId)}
              data-testid={`period-row-${p.periodStart}`}
            >
              <td className={styles.expandCell}>
                {expandedPeriodId === p.periodId ? '▾' : '▸'}
              </td>
              <td>{p.employeeId}</td>
              <td>{p.orgId}</td>
              <td>{formatDate(p.periodStart)} &ndash; {formatDate(p.periodEnd)}</td>
              <td>{PERIOD_TYPE_LABELS[p.periodType] ?? p.periodType}</td>
              <td>{p.agreementCode}</td>
              <td><Badge variant={STATUS_VARIANTS[p.status] ?? 'default'}>{STATUS_LABELS[p.status] ?? p.status}</Badge></td>
              <td>
                <div className={styles.actionCell}>
                  {(p.status === 'SUBMITTED' || p.status === 'EMPLOYEE_APPROVED') && (
                    <>
                      <button
                        className={styles.approveButton}
                        onClick={e => { e.stopPropagation(); onApprove(p.periodId) }}
                        disabled={approvingId === p.periodId}
                      >
                        {approvingId === p.periodId ? 'Godkender...' : 'Godkend'}
                      </button>
                      <button
                        className={styles.rejectButton}
                        onClick={e => { e.stopPropagation(); onReject(p) }}
                      >
                        Afvis
                      </button>
                    </>
                  )}
                  {p.status === 'APPROVED' && hasMinRole(userRole, 'LocalHR') && (
                    <button
                      className={styles.reopenButton}
                      onClick={e => { e.stopPropagation(); onReopen(p.periodId) }}
                      disabled={reopeningId === p.periodId}
                    >
                      {reopeningId === p.periodId ? 'Genåbner...' : 'Genåbn'}
                    </button>
                  )}
                </div>
              </td>
            </tr>
            {expandedPeriodId === p.periodId && (
              <tr className={styles.detailRow}>
                <td colSpan={8}>
                  <ApprovalDetailPanel period={p} />
                </td>
              </tr>
            )}
          </React.Fragment>
        ))}
      </tbody>
    </table>
  )
}

export function ApprovalDashboard() {
  const { role } = useAuth()
  const now = new Date()
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1)

  const {
    periods: allPeriods,
    loading: allLoading,
    error: allError,
    refetch: fetchAll,
  } = useApprovalsByMonth(year, month)

  const {
    periods: myReportPeriods,
    loading: myReportsLoading,
    error: myReportsError,
    refetch: fetchPendingMyReports,
  } = useMyReportsByMonth(year, month)

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

  const showToast = useCallback((message: string, variant: 'success' | 'error') => {
    setToast({ message, variant })
    setTimeout(() => setToast(null), 4000)
  }, [])

  const refreshAll = useCallback(async () => {
    await Promise.all([fetchAll(), fetchPendingMyReports()])
  }, [fetchAll, fetchPendingMyReports])

  const goToPrevMonth = useCallback(() => {
    setMonth(prev => { if (prev === 1) { setYear(y => y - 1); return 12 } return prev - 1 })
  }, [])
  const goToNextMonth = useCallback(() => {
    setMonth(prev => { if (prev === 12) { setYear(y => y + 1); return 1 } return prev + 1 })
  }, [])

  const [reopeningId, setReopeningId] = useState<string | null>(null)
  const handleReopen = async (periodId: string) => {
    setReopeningId(periodId)
    try {
      const result = await apiClient.post<unknown>(`/api/approval/${periodId}/reopen`, { reason: 'Genåbnet af leder' })
      if (!result.ok) throw new Error(result.error)
      showToast('Periode genåbnet.', 'success')
      await refreshAll()
    } catch (e) {
      showToast(String(e instanceof Error ? e.message : e), 'error')
    } finally {
      setReopeningId(null)
    }
  }

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
        <PeriodsTable
          periods={myReportPeriods}
          loading={myReportsLoading}
          error={myReportsError}
          approvingId={approvingId}
          reopeningId={reopeningId}
          userRole={role}
          onApprove={handleApprove}
          onReject={openRejectDialog}
          onReopen={handleReopen}
        />
      ),
    },
    {
      value: 'all',
      label: `Alle i omraade (${allPeriods.length})`,
      content: (
        <PeriodsTable
          periods={allPeriods}
          loading={allLoading}
          error={allError}
          approvingId={approvingId}
          reopeningId={reopeningId}
          userRole={role}
          onApprove={handleApprove}
          onReject={openRejectDialog}
          onReopen={handleReopen}
        />
      ),
    },
  ]

  return (
    <div className={styles.page}>
      <div className={styles.monthNav}>
        <Button variant="ghost" size="sm" onClick={goToPrevMonth}>&larr; Forrige</Button>
        <h2 className={styles.monthTitle}>{formatMonthLabel(year, month)}</h2>
        <Button variant="ghost" size="sm" onClick={goToNextMonth}>Naeste &rarr;</Button>
      </div>

      {toast && (
        <div className={toast.variant === 'success' ? styles.alertSuccess : styles.alert}>
          {toast.message}
        </div>
      )}

      <Tabs tabs={tabs} defaultValue="my-reports" />

      {/* Rejection dialog — kit Dialog (Radix focus-trap/Escape/aria; the built-in
          close-X is the only additive change vs the old plain-div). onOpenChange(false)
          routes to closeRejectDialog so Escape/overlay-click/X clear React state, not
          just the visual close. */}
      <Dialog
        open={rejectTarget !== null}
        onOpenChange={next => { if (!next) closeRejectDialog() }}
        title="Afvis periode"
        description={
          rejectTarget
            ? `Angiv begrundelse for afvisning af perioden for ${rejectTarget.employeeId} (${formatDate(rejectTarget.periodStart)} – ${formatDate(rejectTarget.periodEnd)}).`
            : undefined
        }
      >
        {rejectTarget && (
          <>
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
          </>
        )}
      </Dialog>

      {/* Enforcement confirmation dialog (428 response) — kit Dialog.
          onOpenChange(false) routes to closeEnforcementDialog. */}
      <Dialog
        open={enforcementDialog !== null}
        onOpenChange={next => { if (!next) closeEnforcementDialog() }}
        title="Haandhaevelse aktiv"
        description={
          enforcementDialog
            ? `Godkendelse kraever den udpegede leder. Du er ikke den udpegede leder for denne medarbejder. Vil du ${enforcementDialog.action === 'approve' ? 'godkende' : 'afvise'} alligevel med organisationskopet?`
            : undefined
        }
      >
        {enforcementDialog && (
          <>
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
          </>
        )}
      </Dialog>
    </div>
  )
}

