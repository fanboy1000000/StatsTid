import { useState, useEffect, useCallback, useRef } from 'react'
import { useAuth } from '../../hooks/useAuth'
import { apiClient } from '../../lib/api'
import type { components } from '../../lib/api-types'
import styles from './MyPeriods.module.css'

// S116 / TASK-11602 (L2) — the GENERATED spec record for the
// GET /api/approval/{employeeId} element. The hand-written 14-field interface
// that lived here exactly matched this shape (it set the consolidation
// direction) and was deleted in its favor.
type ApprovalPeriod =
  components['schemas']['StatsTid.Backend.Api.Contracts.EmployeePeriodItem']

// S127 / TASK-12707 (owner ruling R3) — the free-range "Indsend periode" form is
// GONE, and `AGREEMENT_CODES` went with it: an employee never picked their own
// overenskomst, and the server has always resolved it. `PERIOD_TYPES` survives as
// the read-only display mapping for the `Type` column — legacy WEEKLY rows still
// exist, and `/api/approval/send` produces MONTHLY only.
const PERIOD_TYPES = [
  { value: 'WEEKLY', label: 'Ugentlig' },
  { value: 'MONTHLY', label: 'Maanedlig' },
]

// S127 / TASK-12707 — `EMPLOYEE_APPROVED` added to BOTH switches. It was missing
// from each and fell through to `default`, which returned the raw enum string; a
// live bug the moment sending began landing rows in that state (before S127 the
// two-step Skema flow reached it too, so this was already wrong here).
// The badge is `badgeWarning`, the same "with someone else, not yet decided" tone
// as SUBMITTED. The label is "Indsendt" — from the EMPLOYEE'S side this month has
// been sent and is awaiting their leader, which is exactly what SUBMITTED meant
// to them; the distinction between the two states is a backend/manager concern
// and inventing a second Danish word for it here would be a distinction without a
// difference for this reader. (`SkemaPage`'s footer already says "Indsendt" for
// EMPLOYEE_APPROVED, so this keeps the two employee surfaces consistent.)
function statusBadgeClass(status: string): string {
  switch (status) {
    case 'DRAFT': return styles.badgeDefault
    case 'SUBMITTED': return styles.badgeWarning
    case 'EMPLOYEE_APPROVED': return styles.badgeWarning
    case 'APPROVED': return styles.badgeSuccess
    case 'REJECTED': return styles.badgeError
    default: return styles.badgeDefault
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case 'DRAFT': return 'Kladde'
    case 'SUBMITTED': return 'Indsendt'
    case 'EMPLOYEE_APPROVED': return 'Indsendt'
    case 'APPROVED': return 'Godkendt'
    case 'REJECTED': return 'Afvist'
    default: return status
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

export function MyPeriods() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''

  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [successMsg, setSuccessMsg] = useState<string | null>(null)

  // S127 / TASK-12707 (R3) — the form state is GONE: periodType / periodStart /
  // periodEnd / agreementCode / okVersion / submitting / formError, plus the
  // `orgId` read that fed the retired body. This page no longer originates a
  // send; Skema does, over the month it is showing. `useAuth` stays for
  // `user.employeeId`, which the list read is keyed on.

  // Track which row is being resubmitted
  const [resubmittingId, setResubmittingId] = useState<string | null>(null)

    const latestPeriodsRequestId = useRef(0)

const fetchPeriods = useCallback(async () => {
    if (!employeeId) return
    const requestId = ++latestPeriodsRequestId.current
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/approval/{employeeId}', {
      params: { path: { employeeId } },
    })
    // S126 / F2 — a newer request superseded this one while it was in flight; drop it.
    if (requestId !== latestPeriodsRequestId.current) return
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchPeriods() }, [fetchPeriods])

  // S127 / TASK-12707 (R3) — `submitPeriod` DELETED with its route. The
  // caller-supplied date range it posted to `POST /api/approval/submit` was the
  // defect S127 closes: it let an employee certify an arbitrary window that no
  // coverage or allocation rule was written for. The one remaining send from
  // this page is the by-id arm below, over a period that already exists.

  const resubmitPeriod = async (periodId: string) => {
    // S116 (L1 fix) — same overclaim corrected: the typed form derives the
    // `{periodId, status}` reality. (This call never sent a body — no delta.)
    const result = await apiClient.post('/api/approval/{periodId}/employee-approve', {
      params: { path: { periodId } },
    })
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const handleResubmit = async (periodId: string) => {
    setResubmittingId(periodId)
    setSuccessMsg(null)
    try {
      await resubmitPeriod(periodId)
      setSuccessMsg('Periode genindsendt.')
      await fetchPeriods()
    } catch (e) {
      setError(String(e instanceof Error ? e.message : e))
    } finally {
      setResubmittingId(null)
    }
  }

  return (
    <div className={styles.page}>
      <h2 className={styles.pageTitle}>Mine perioder</h2>

      {successMsg && (
        <div className={styles.alertSuccess}>{successMsg}</div>
      )}

      {/* S127 / TASK-12707 (owner ruling R3) — the "Indsend periode" card is
          REMOVED. It let an employee send an arbitrary date range typed into two
          date inputs; the send command validates a whole month, so the form could
          only ever produce rows the new rule was not written for. Sending now
          happens on Skema, over the month being looked at. What stays is this
          page's honest job: the list, and re-sending a month that came back. */}

      {/* Periods table */}
      <div className={styles.card}>
        <h3 className={styles.cardHeader}>Perioder</h3>

        {loading && (
          <div className={styles.spinner}>Henter perioder...</div>
        )}

        {error && (
          <div className={styles.alert}>{error}</div>
        )}

        {!loading && !error && periods.length === 0 && (
          <p className={styles.emptyState}>Ingen perioder fundet.</p>
        )}

        {periods.length > 0 && (
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Periode</th>
                <th>Type</th>
                <th>Status</th>
                <th>Overenskomst</th>
                <th>Indsendt</th>
                <th>Godkendt af</th>
                <th>Afvisningsgrund</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {periods.map(p => (
                <tr key={p.periodId}>
                  <td>{formatDate(p.periodStart)} &ndash; {formatDate(p.periodEnd)}</td>
                  <td>{PERIOD_TYPES.find(t => t.value === p.periodType)?.label ?? p.periodType}</td>
                  <td>
                    <span className={`${styles.badge} ${statusBadgeClass(p.status)}`}>
                      {statusLabel(p.status)}
                    </span>
                  </td>
                  <td>{p.agreementCode}</td>
                  <td>{formatDate(p.submittedAt)}</td>
                  <td>{p.approvedBy ?? '-'}</td>
                  <td>
                    {p.rejectionReason
                      ? <span className={styles.rejectionText}>{p.rejectionReason}</span>
                      : '-'}
                  </td>
                  <td>
                    {(p.status === 'DRAFT' || p.status === 'REJECTED') && (
                      <button
                        className={styles.resubmitButton}
                        onClick={() => handleResubmit(p.periodId)}
                        disabled={resubmittingId === p.periodId}
                      >
                        {resubmittingId === p.periodId ? 'Indsender...' : 'Indsend'}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
