import { useState, useCallback, useMemo } from 'react'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import { formatMonthLabel } from '../../lib/locale'
import { apiClient } from '../../lib/api'
import { Dialog } from '../../components/ui/Dialog'
import { useTeamOverview, type TeamOverviewRow } from '../../hooks/useTeamOverview'
import styles from './TeamOversigt.module.css'

// ── Status mapping (the 4 display statuses) ──────────────────────────────────
// SUBMITTED + EMPLOYEE_APPROVED → Indsendt (the leader-approves bucket);
// APPROVED → Godkendt; REJECTED → Afvist; DRAFT → Kladde.
type DisplayStatus = 'Indsendt' | 'Godkendt' | 'Afvist' | 'Kladde'

interface StatusMeta {
  label: DisplayStatus
  badgeClass: string
  /** Sort rank: Indsendt 0, Afvist 1, Godkendt 2, Kladde 3 (per the hifi). */
  rank: number
  /** A pending (leader-approvable) row → has Godkend/Afvis actions + selectable. */
  isPending: boolean
  /** A decided row (Godkendt/Afvist) → reopen-eligible (LocalHR+ only). */
  isDecided: boolean
  isDraft: boolean
}

function statusMeta(status: TeamOverviewRow['status']): StatusMeta {
  switch (status) {
    case 'SUBMITTED':
    case 'EMPLOYEE_APPROVED':
      return { label: 'Indsendt', badgeClass: styles.badgeIndsendt, rank: 0, isPending: true, isDecided: false, isDraft: false }
    case 'APPROVED':
      return { label: 'Godkendt', badgeClass: styles.badgeGodkendt, rank: 2, isPending: false, isDecided: true, isDraft: false }
    case 'REJECTED':
      return { label: 'Afvist', badgeClass: styles.badgeAfvist, rank: 1, isPending: false, isDecided: true, isDraft: false }
    default:
      return { label: 'Kladde', badgeClass: styles.badgeKladde, rank: 3, isPending: false, isDecided: false, isDraft: true }
  }
}

// ── Danish number formatting (decimal comma, 1 dp) ───────────────────────────
function daNum(n: number, dec = 1): string {
  return Number(n).toFixed(dec).replace('.', ',')
}

function flexText(flex: number): string {
  return (flex >= 0 ? '+' : '−') + daNum(Math.abs(flex)) + ' t'
}

function flexColorClass(flex: number): string {
  if (flex > 0.05) return styles.flexPositive
  if (flex < -0.05) return styles.flexNegative
  return styles.flexZero
}

type FilterKey = 'alle' | 'afventer' | 'godkendt' | 'advarsel'
type SortKey = 'navn' | 'status' | 'norm' | 'flex'
type SortDir = 'asc' | 'desc'

/** Per-row bulk outcome surfaced after a bulk-approve loop. */
type BulkOutcome = 'approved' | 'conflict' | 'enforcement'

export function TeamOversigt() {
  const { role, orgId } = useAuth()
  const isHrPlus = hasMinRole(role, 'LocalHR')

  const now = new Date()
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1)

  const { rows, loading, error, refetch } = useTeamOverview(year, month)

  // Toolbar / view state
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<FilterKey>('alle')
  const [sortKey, setSortKey] = useState<SortKey>('status')
  const [sortDir, setSortDir] = useState<SortDir>('asc')
  const [selected, setSelected] = useState<Record<string, boolean>>({})

  // Reject dialog state
  const [rejectTarget, setRejectTarget] = useState<TeamOverviewRow | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)

  // Enforcement (428) confirmation dialog state
  const [enforcement, setEnforcement] = useState<{
    row: TeamOverviewRow
    action: 'approve' | 'reject'
    designatedApproverId: string | null
    reason?: string
  } | null>(null)
  const [enforcementConfirming, setEnforcementConfirming] = useState(false)

  // Per-action busy + toast
  const [busyId, setBusyId] = useState<string | null>(null)
  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null)

  // Bulk state
  const [bulkRunning, setBulkRunning] = useState(false)
  const [bulkResults, setBulkResults] = useState<Record<string, BulkOutcome>>({})

  const showToast = useCallback((message: string, variant: 'success' | 'error') => {
    setToast({ message, variant })
    setTimeout(() => setToast(null), 4000)
  }, [])

  const goPrevMonth = useCallback(() => {
    setMonth(prev => { if (prev === 1) { setYear(y => y - 1); return 12 } return prev - 1 })
  }, [])
  const goNextMonth = useCallback(() => {
    setMonth(prev => { if (prev === 12) { setYear(y => y + 1); return 1 } return prev + 1 })
  }, [])

  // ── KPIs (FULL team, unfiltered) ───────────────────────────────────────────
  const kpiAfventer = rows.filter(r => statusMeta(r.status).isPending).length
  const kpiAdvarsler = rows.filter(r => r.hasWarning).length
  const kpiGodkendt = rows.filter(r => r.status === 'APPROVED').length
  const kpiFravaer = rows.filter(r => r.awayToday).length
  const kpiNorm = rows.length > 0
    ? Math.round(rows.reduce((s, r) => s + (r.normExpected > 0 ? r.normRegistered / r.normExpected : 0), 0) / rows.length * 100)
    : 0

  // ── Filter + sort ──────────────────────────────────────────────────────────
  const view = useMemo(() => {
    const q = search.trim().toLowerCase()
    const filtered = rows.filter(r => {
      if (q && !`${r.displayName} ${r.employeeId}`.toLowerCase().includes(q)) return false
      if (filter === 'afventer') return statusMeta(r.status).isPending
      if (filter === 'godkendt') return r.status === 'APPROVED'
      if (filter === 'advarsel') return r.hasWarning
      return true
    })
    const dir = sortDir === 'asc' ? 1 : -1
    return [...filtered].sort((a, b) => {
      let av: number | string
      let bv: number | string
      if (sortKey === 'navn') { av = a.displayName; bv = b.displayName }
      else if (sortKey === 'status') { av = statusMeta(a.status).rank; bv = statusMeta(b.status).rank }
      else if (sortKey === 'norm') {
        av = a.normExpected > 0 ? a.normRegistered / a.normExpected : 0
        bv = b.normExpected > 0 ? b.normRegistered / b.normExpected : 0
      } else { av = a.flexBalance; bv = b.flexBalance }
      if (av < bv) return -1 * dir
      if (av > bv) return 1 * dir
      return 0
    })
  }, [rows, search, filter, sortKey, sortDir])

  // ── Bulk selection (visible pending only) ──────────────────────────────────
  const pendingVisible = view.filter(r => statusMeta(r.status).isPending && r.periodId)
  const allChecked = pendingVisible.length > 0 && pendingVisible.every(r => selected[r.employeeId])
  const selectedCount = Object.values(selected).filter(Boolean).length

  const toggleAll = () => {
    setSelected(prev => {
      const next = { ...prev }
      const target = !allChecked
      pendingVisible.forEach(r => { next[r.employeeId] = target })
      return next
    })
  }
  const toggleOne = (employeeId: string) => {
    setSelected(prev => ({ ...prev, [employeeId]: !prev[employeeId] }))
  }
  const clearSelection = (employeeId: string) => {
    setSelected(prev => { const next = { ...prev }; delete next[employeeId]; return next })
  }

  const sortBy = (k: SortKey) => {
    setSortDir(prev => (sortKey === k && prev === 'asc' ? 'desc' : 'asc'))
    setSortKey(k)
  }
  const arrow = (k: SortKey) => (sortKey === k ? (sortDir === 'asc' ? ' ↑' : ' ↓') : '')

  // ── Status-aware single approve (mirrors ApprovalDashboard.tsx:230) ─────────
  // Distinguishes 200 (ok) / 428 (enforcement fallback → confirm) / 409 (lost
  // race) / other. Returns the outcome so the bulk loop can aggregate.
  const approveOne = useCallback(async (
    row: TeamOverviewRow,
    confirmFallback?: boolean,
  ): Promise<'approved' | 'enforcement' | 'conflict' | 'error'> => {
    if (!row.periodId) return 'error'
    const url = confirmFallback
      ? `/api/approval/${row.periodId}/approve?confirmFallback=true`
      : `/api/approval/${row.periodId}/approve`
    const result = await apiClient.post<unknown>(url)
    if (result.ok) return 'approved'
    if (result.status === 428) {
      let body: { designatedApproverId?: string | null } = {}
      try { body = JSON.parse(result.error) as typeof body } catch { /* ignore */ }
      setEnforcement({ row, action: 'approve', designatedApproverId: body.designatedApproverId ?? null })
      return 'enforcement'
    }
    if (result.status === 409) return 'conflict'
    return 'error'
  }, [])

  const handleApprove = async (row: TeamOverviewRow) => {
    if (!row.periodId) return
    setBusyId(row.employeeId)
    try {
      const outcome = await approveOne(row)
      if (outcome === 'approved') {
        clearSelection(row.employeeId)
        showToast(`${row.displayName} godkendt.`, 'success')
        await refetch()
      } else if (outcome === 'conflict') {
        showToast(`${row.displayName}: perioden er ændret af en anden. Genindlæser.`, 'error')
        await refetch()
      } else if (outcome === 'error') {
        showToast(`Kunne ikke godkende ${row.displayName}.`, 'error')
      }
      // 'enforcement' → dialog opened; resolved via handleEnforcementConfirm.
    } finally {
      setBusyId(null)
    }
  }

  // ── Reject (kit Radix Dialog, optional reason) ─────────────────────────────
  const openReject = (row: TeamOverviewRow) => {
    setRejectTarget(row)
    setRejectReason(row.rejectionReason ?? '')
  }
  const closeReject = () => {
    setRejectTarget(null)
    setRejectReason('')
    setRejecting(false)
  }

  const handleReject = async () => {
    if (!rejectTarget || !rejectTarget.periodId) return
    setRejecting(true)
    const reason = rejectReason.trim()
    const result = await apiClient.post<unknown>(
      `/api/approval/${rejectTarget.periodId}/reject`,
      { reason },
    )
    if (result.ok) {
      clearSelection(rejectTarget.employeeId)
      showToast(`${rejectTarget.displayName} afvist.`, 'success')
      closeReject()
      await refetch()
      return
    }
    if (result.status === 428) {
      let body: { designatedApproverId?: string | null } = {}
      try { body = JSON.parse(result.error) as typeof body } catch { /* ignore */ }
      setEnforcement({
        row: rejectTarget,
        action: 'reject',
        designatedApproverId: body.designatedApproverId ?? null,
        reason,
      })
      closeReject()
      return
    }
    if (result.status === 409) {
      showToast(`${rejectTarget.displayName}: perioden er ændret af en anden. Genindlæser.`, 'error')
      closeReject()
      await refetch()
      return
    }
    showToast(`Kunne ikke afvise ${rejectTarget.displayName}.`, 'error')
    setRejecting(false)
  }

  // ── Reopen (LocalHR+ only) ─────────────────────────────────────────────────
  const handleReopen = async (row: TeamOverviewRow) => {
    if (!row.periodId) return
    setBusyId(row.employeeId)
    try {
      const result = await apiClient.post<unknown>(
        `/api/approval/${row.periodId}/reopen`,
        { reason: 'Genåbnet af leder' },
      )
      if (result.ok) {
        showToast(`${row.displayName} genåbnet.`, 'success')
        await refetch()
      } else {
        showToast(`Kunne ikke genåbne ${row.displayName}.`, 'error')
      }
    } finally {
      setBusyId(null)
    }
  }

  // ── Enforcement (428) confirm ──────────────────────────────────────────────
  const closeEnforcement = () => {
    setEnforcement(null)
    setEnforcementConfirming(false)
  }
  const handleEnforcementConfirm = async () => {
    if (!enforcement) return
    setEnforcementConfirming(true)
    const { row, action, reason } = enforcement
    const url = action === 'approve'
      ? `/api/approval/${row.periodId}/approve?confirmFallback=true`
      : `/api/approval/${row.periodId}/reject?confirmFallback=true`
    const result = await apiClient.post<unknown>(url, action === 'reject' ? { reason } : undefined)
    if (result.ok) {
      clearSelection(row.employeeId)
      showToast(`${row.displayName} ${action === 'approve' ? 'godkendt' : 'afvist'}.`, 'success')
      closeEnforcement()
      await refetch()
    } else {
      showToast(`Handlingen mislykkedes for ${row.displayName}.`, 'error')
      setEnforcementConfirming(false)
    }
  }

  // ── Bulk approve (FE loop of the hardened single-approve, sequential) ───────
  const handleBulkApprove = async () => {
    const targets = rows.filter(r => selected[r.employeeId] && statusMeta(r.status).isPending && r.periodId)
    if (targets.length === 0) return
    setBulkRunning(true)
    setBulkResults({})
    const results: Record<string, BulkOutcome> = {}
    const succeeded: string[] = []
    let enforcementHit = false
    for (const row of targets) {
      // Sequential by design (same tree advisory lock) — do NOT parallelize.
      const outcome = await approveOne(row)
      if (outcome === 'approved') {
        results[row.employeeId] = 'approved'
        succeeded.push(row.employeeId)
      } else if (outcome === 'conflict') {
        results[row.employeeId] = 'conflict'
      } else if (outcome === 'enforcement') {
        // approveOne already opened the enforcement dialog for the first such row.
        results[row.employeeId] = 'enforcement'
        enforcementHit = true
      }
      // 'error' → leave it out of results (transient); the row stays selected.
    }
    // Clear selection of the succeeded rows.
    setSelected(prev => {
      const next = { ...prev }
      succeeded.forEach(id => { delete next[id] })
      return next
    })
    setBulkResults(results)
    setBulkRunning(false)
    const okCount = succeeded.length
    const conflictCount = Object.values(results).filter(o => o === 'conflict').length
    const parts: string[] = []
    if (okCount > 0) parts.push(`${okCount} godkendt`)
    if (conflictCount > 0) parts.push(`${conflictCount} sprang over (ændret)`)
    if (enforcementHit) parts.push('1 kræver bekræftelse')
    if (parts.length > 0) {
      showToast(parts.join(' · '), conflictCount > 0 || enforcementHit ? 'error' : 'success')
    }
    await refetch()
  }

  const isEmpty = view.length === 0
  const monthLabel = formatMonthLabel(year, month)
  const teamCount = rows.length

  const filterDef: { key: FilterKey; label: string; count: number }[] = [
    { key: 'alle', label: 'Alle', count: rows.length },
    { key: 'afventer', label: 'Afventer', count: kpiAfventer },
    { key: 'godkendt', label: 'Godkendt', count: kpiGodkendt },
    { key: 'advarsel', label: 'Advarsel', count: kpiAdvarsler },
  ]

  return (
    <div className={styles.page}>
      {/* Page header */}
      <div className={styles.pageHeader}>
        <div>
          <h2 className={styles.title}>Teamoversigt</h2>
          <p className={styles.subline}>
            {orgId ? `${orgId} · ` : ''}{teamCount} medarbejdere
          </p>
        </div>
        <div className={styles.monthStepper}>
          <button type="button" className={styles.stepperBtn} onClick={goPrevMonth}>
            &larr; Forrige
          </button>
          <span className={styles.stepperLabel} data-testid="month-label">{monthLabel}</span>
          <button type="button" className={styles.stepperBtn} onClick={goNextMonth}>
            Næste &rarr;
          </button>
        </div>
      </div>

      {toast && (
        <div className={toast.variant === 'success' ? styles.alertSuccess : styles.alert} role="status">
          {toast.message}
        </div>
      )}

      {/* KPI band */}
      <div className={styles.kpiBand}>
        <div className={`${styles.kpiCard} ${styles.kpiCardPrimary}`}>
          <p className={styles.kpiLabel}>Afventer din godkendelse</p>
          <p className={`${styles.kpiValue} ${styles.kpiValuePrimary}`}>{kpiAfventer}</p>
        </div>
        <div className={styles.kpiCard}>
          <p className={styles.kpiLabel}>Advarsler</p>
          <p className={`${styles.kpiValue} ${kpiAdvarsler > 0 ? styles.kpiValueWarning : ''}`}>{kpiAdvarsler}</p>
        </div>
        <div className={styles.kpiCard}>
          <p className={styles.kpiLabel}>Norm-opfyldelse</p>
          <p className={styles.kpiValue}>{kpiNorm}<span className={styles.kpiSuffix}>%</span></p>
        </div>
        <div className={styles.kpiCard}>
          <p className={styles.kpiLabel}>Fravær i dag</p>
          <p className={styles.kpiValue}>{kpiFravaer}</p>
        </div>
        <div className={styles.kpiCard}>
          <p className={styles.kpiLabel}>Godkendt</p>
          <p className={styles.kpiValue}>{kpiGodkendt}<span className={styles.kpiSuffix}> / {teamCount}</span></p>
        </div>
      </div>

      {/* Toolbar */}
      <div className={styles.toolbar}>
        <input
          className={styles.search}
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder="Søg medarbejder…"
          aria-label="Søg medarbejder"
        />
        <div className={styles.chips}>
          {filterDef.map(f => (
            <button
              key={f.key}
              type="button"
              className={`${styles.chip} ${filter === f.key ? styles.chipActive : ''}`}
              onClick={() => setFilter(f.key)}
              aria-pressed={filter === f.key}
            >
              {f.label} <span className={styles.chipCount}>{f.count}</span>
            </button>
          ))}
        </div>
        <div className={styles.toolbarSpacer} />
        {selectedCount > 0 && (
          <button
            type="button"
            className={styles.bulkBtn}
            onClick={handleBulkApprove}
            disabled={bulkRunning}
          >
            {bulkRunning ? 'Godkender…' : `Godkend ${selectedCount} valgte`}
          </button>
        )}
      </div>

      {/* Table */}
      <div className={styles.tableCard}>
        {error ? (
          <div className={styles.alert} role="alert">{error}</div>
        ) : loading ? (
          <div className={styles.emptyTable}>Henter teamoversigt…</div>
        ) : (
          <table className={styles.table}>
            <thead>
              <tr className={styles.headRow}>
                <th className={styles.checkboxCell}>
                  <input
                    type="checkbox"
                    checked={allChecked}
                    onChange={toggleAll}
                    disabled={pendingVisible.length === 0}
                    aria-label="Vælg alle"
                  />
                </th>
                <th className={styles.sortable} onClick={() => sortBy('navn')}>Medarbejder{arrow('navn')}</th>
                <th>Overenskomst</th>
                <th className={styles.sortable} onClick={() => sortBy('status')}>Status{arrow('status')}</th>
                <th className={styles.sortable} onClick={() => sortBy('norm')}>Norm / registreret{arrow('norm')}</th>
                <th className={`${styles.sortable} ${styles.right}`} onClick={() => sortBy('flex')}>Flex{arrow('flex')}</th>
                <th>Ferie</th>
                <th>Advarsler</th>
                <th className={styles.handlingHead}>Handling</th>
              </tr>
            </thead>
            <tbody>
              {view.map(row => {
                const meta = statusMeta(row.status)
                const ratio = row.normExpected > 0 ? row.normRegistered / row.normExpected : 0
                const barColor = ratio >= 1 ? styles.barGreen : ratio >= 0.95 ? styles.barInfo : styles.barWarn
                const checked = !!selected[row.employeeId]
                const bulkOutcome = bulkResults[row.employeeId]
                return (
                  <tr
                    key={row.employeeId}
                    className={`${styles.bodyRow} ${checked ? styles.rowSelected : ''}`}
                    data-testid={`team-row-${row.employeeId}`}
                  >
                    <td className={styles.checkboxCell}>
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={() => toggleOne(row.employeeId)}
                        disabled={!meta.isPending || !row.periodId}
                        aria-label={`Vælg ${row.displayName}`}
                      />
                    </td>
                    <td>
                      <span className={styles.empName}>{row.displayName}</span>
                      <span className={styles.empId}>{row.employeeId}</span>
                    </td>
                    <td className={styles.secondary}>{row.agreement}</td>
                    <td>
                      <span className={`${styles.badge} ${meta.badgeClass}`}>{meta.label}</span>
                    </td>
                    <td className={styles.nowrap}>
                      <div>{daNum(row.normRegistered)} / {daNum(row.normExpected)} t</div>
                      <div className={styles.barTrack}>
                        <div
                          className={`${styles.barFill} ${barColor}`}
                          style={{ width: `${Math.min(100, Math.round(ratio * 100))}%` }}
                        />
                      </div>
                    </td>
                    <td className={`${styles.right} ${styles.flexCell} ${flexColorClass(row.flexBalance)}`}>
                      {flexText(row.flexBalance)}
                    </td>
                    <td className={styles.secondary}>{row.ferieUsed} / {row.ferieTotal} dage</td>
                    <td>
                      {row.hasWarning ? (
                        <span className={styles.warnChip} title="Manglende fordeling på projekter">
                          Manglende fordeling
                        </span>
                      ) : (
                        <span className={styles.emDash}>—</span>
                      )}
                    </td>
                    <td className={styles.handlingCell}>
                      {meta.isPending && row.periodId ? (
                        <div className={styles.handlingActions}>
                          <button
                            type="button"
                            className={styles.approveBtn}
                            onClick={() => handleApprove(row)}
                            disabled={busyId === row.employeeId || bulkRunning}
                          >
                            Godkend
                          </button>
                          <button
                            type="button"
                            className={styles.rejectBtn}
                            onClick={() => openReject(row)}
                            disabled={busyId === row.employeeId || bulkRunning}
                          >
                            Afvis
                          </button>
                          {bulkOutcome === 'conflict' && <span className={styles.outcomeConflict}>Ændret</span>}
                        </div>
                      ) : meta.isDecided && row.periodId ? (
                        isHrPlus ? (
                          <button
                            type="button"
                            className={styles.reopenBtn}
                            onClick={() => handleReopen(row)}
                            disabled={busyId === row.employeeId}
                          >
                            Genåbn
                          </button>
                        ) : null
                      ) : (
                        <span className={styles.notSubmitted}>Ikke indsendt</span>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
        {!loading && !error && isEmpty && (
          <div className={styles.emptyTable}>Ingen medarbejdere matcher søgningen.</div>
        )}
      </div>

      {/* Reject dialog (kit Radix Dialog, optional reason) */}
      <Dialog
        open={rejectTarget !== null}
        onOpenChange={next => { if (!next) closeReject() }}
        title="Afvis måned"
        description={
          rejectTarget
            ? `Du er ved at afvise ${rejectTarget.displayName}s registrering for ${monthLabel}. Medarbejderen kan herefter rette og indsende måneden igen.`
            : undefined
        }
      >
        {rejectTarget && (
          <>
            <label className={styles.dialogLabel} htmlFor="reject-reason">
              Begrundelse <span className={styles.dialogLabelOptional}>(valgfri)</span>
            </label>
            <textarea
              id="reject-reason"
              className={styles.dialogTextarea}
              rows={3}
              value={rejectReason}
              onChange={e => setRejectReason(e.target.value)}
              placeholder="Skriv en kort begrundelse til medarbejderen…"
              autoFocus
            />
            <div className={styles.dialogActions}>
              <button type="button" className={styles.cancelBtn} onClick={closeReject}>
                Annullér
              </button>
              <button
                type="button"
                className={styles.confirmRejectBtn}
                onClick={handleReject}
                disabled={rejecting}
              >
                {rejecting ? 'Afviser…' : 'Afvis måned'}
              </button>
            </div>
          </>
        )}
      </Dialog>

      {/* Enforcement (428) confirmation dialog */}
      <Dialog
        open={enforcement !== null}
        onOpenChange={next => { if (!next) closeEnforcement() }}
        title="Håndhævelse aktiv"
        description={
          enforcement
            ? `Du er ikke den udpegede leder for ${enforcement.row.displayName}. Vil du ${enforcement.action === 'approve' ? 'godkende' : 'afvise'} alligevel med organisationsskopet?`
            : undefined
        }
      >
        {enforcement && (
          <>
            {enforcement.designatedApproverId && (
              <p className={styles.dialogDescription}>
                Udpeget leder: {enforcement.designatedApproverId}
              </p>
            )}
            <div className={styles.dialogActions}>
              <button type="button" className={styles.cancelBtn} onClick={closeEnforcement}>
                Annullér
              </button>
              <button
                type="button"
                className={styles.approveBtn}
                onClick={handleEnforcementConfirm}
                disabled={enforcementConfirming}
              >
                {enforcementConfirming
                  ? (enforcement.action === 'approve' ? 'Godkender…' : 'Afviser…')
                  : (enforcement.action === 'approve' ? 'Godkend alligevel' : 'Afvis alligevel')}
              </button>
            </div>
          </>
        )}
      </Dialog>
    </div>
  )
}
