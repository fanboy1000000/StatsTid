import { useState, useEffect, useCallback, type FormEvent } from 'react'
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

const PERIOD_TYPES = [
  { value: 'WEEKLY', label: 'Ugentlig' },
  { value: 'MONTHLY', label: 'Maanedlig' },
]

// (S116: `as const` dropped — the lint tier bans every TSAsExpression; the
// explicit readonly annotation carries the same immutability for this use.)
const AGREEMENT_CODES: readonly string[] = ['AC', 'HK', 'PROSA']

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'DRAFT': return styles.badgeDefault
    case 'SUBMITTED': return styles.badgeWarning
    case 'APPROVED': return styles.badgeSuccess
    case 'REJECTED': return styles.badgeError
    default: return styles.badgeDefault
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case 'DRAFT': return 'Kladde'
    case 'SUBMITTED': return 'Indsendt'
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
  const { user, orgId } = useAuth()
  const employeeId = user?.employeeId ?? ''

  const [periods, setPeriods] = useState<ApprovalPeriod[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [successMsg, setSuccessMsg] = useState<string | null>(null)

  // Form state
  const [periodType, setPeriodType] = useState('WEEKLY')
  const [periodStart, setPeriodStart] = useState('')
  const [periodEnd, setPeriodEnd] = useState('')
  const [agreementCode, setAgreementCode] = useState('AC')
  const [okVersion] = useState('OK24')
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  // Track which row is being resubmitted
  const [resubmittingId, setResubmittingId] = useState<string | null>(null)

  const fetchPeriods = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/approval/{employeeId}', {
      params: { path: { employeeId } },
    })
    if (result.ok) {
      setPeriods(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => { fetchPeriods() }, [fetchPeriods])

  const submitPeriod = async (payload: {
    employeeId: string
    orgId: string
    periodStart: string
    periodEnd: string
    periodType: string
    agreementCode: string
    okVersion: string
  }) => {
    // S116 (L1 fix) — the legacy call overclaimed `<ApprovalPeriod>`; the
    // backend returns only `{periodId, status}` (the spec PeriodActionResponse,
    // derived by the typed form). Callers discard the return value, so no
    // consumer ever read the phantom fields.
    const result = await apiClient.post('/api/approval/submit', { body: payload })
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const resubmitPeriod = async (periodId: string) => {
    // S116 (L1 fix) — same overclaim corrected: the typed form derives the
    // `{periodId, status}` reality. (This call never sent a body — no delta.)
    const result = await apiClient.post('/api/approval/{periodId}/employee-approve', {
      params: { path: { periodId } },
    })
    if (!result.ok) throw new Error(result.error)
    return result.data
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)
    setSuccessMsg(null)

    if (!periodStart || !periodEnd) {
      setFormError('Udfyld venligst start- og slutdato.')
      return
    }

    // The submit endpoint requires the period's owning org (SubmitPeriodRequest.OrgId);
    // an employee submits for their own primary org, taken from the auth context.
    if (!orgId) {
      setFormError('Kunne ikke fastslaa organisation. Log ind igen.')
      return
    }

    setSubmitting(true)
    try {
      await submitPeriod({
        employeeId,
        orgId,
        periodStart,
        periodEnd,
        periodType,
        agreementCode,
        okVersion,
      })
      setSuccessMsg('Periode indsendt.')
      setPeriodStart('')
      setPeriodEnd('')
      await fetchPeriods()
    } catch (e) {
      setFormError(String(e instanceof Error ? e.message : e))
    } finally {
      setSubmitting(false)
    }
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

      {/* Submit period form */}
      <div className={styles.card}>
        <h3 className={styles.cardHeader}>Indsend periode</h3>
        <form className={styles.form} onSubmit={handleSubmit}>
          <div className={styles.formRow}>
            <label className={`${styles.formLabel} required`} htmlFor="periodType">
              Periodetype
            </label>
            <select
              id="periodType"
              className={styles.formSelect}
              value={periodType}
              onChange={e => setPeriodType(e.target.value)}
            >
              {PERIOD_TYPES.map(t => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </select>
          </div>

          <div className={styles.formRow}>
            <label className={`${styles.formLabel} required`} htmlFor="periodStart">
              Startdato
            </label>
            <input
              id="periodStart"
              type="date"
              className={styles.formInput}
              value={periodStart}
              onChange={e => setPeriodStart(e.target.value)}
              required
            />
          </div>

          <div className={styles.formRow}>
            <label className={`${styles.formLabel} required`} htmlFor="periodEnd">
              Slutdato
            </label>
            <input
              id="periodEnd"
              type="date"
              className={styles.formInput}
              value={periodEnd}
              onChange={e => setPeriodEnd(e.target.value)}
              required
            />
          </div>

          <div className={styles.formRow}>
            <label className={styles.formLabel} htmlFor="agreementCode">
              Overenskomst
            </label>
            <select
              id="agreementCode"
              className={styles.formSelect}
              value={agreementCode}
              onChange={e => setAgreementCode(e.target.value)}
            >
              {AGREEMENT_CODES.map(c => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          </div>

          <div className={styles.formRow}>
            <label className={styles.formLabel} htmlFor="okVersion">
              OK version
            </label>
            <input
              id="okVersion"
              type="text"
              className={styles.formInput}
              value={okVersion}
              disabled
            />
          </div>

          {formError && (
            <div className={styles.alert}>{formError}</div>
          )}

          <button
            type="submit"
            className={styles.submitButton}
            disabled={submitting}
          >
            {submitting ? 'Indsender...' : 'Indsend periode'}
          </button>
        </form>
      </div>

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
