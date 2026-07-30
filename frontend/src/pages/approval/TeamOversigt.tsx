import { useState, useCallback, useMemo, useRef, useEffect, Fragment, type ReactNode } from 'react'
import { useAuth } from '../../contexts/AuthContext'
import { formatMonthLabel } from '../../lib/locale'
import { apiClient } from '../../lib/api'
import { Dialog } from '../../components/ui/Dialog'
import { ManagerSkemaGrid } from './ManagerSkemaGrid'
import { useTeamOverview, type TeamOverviewRow } from '../../hooks/useTeamOverview'
import { useAllocationBreakdown } from '../../hooks/useAllocationBreakdown'
import { useCompliance } from '../../hooks/useCompliance'
import styles from './TeamOversigt.module.css'

// ── Status mapping (the 4 display statuses) ──────────────────────────────────
// SUBMITTED + EMPLOYEE_APPROVED → Indsendt (the leader-approves bucket);
// APPROVED → Godkendt; REJECTED → Afvist; DRAFT → "Ikke indsendt".
// S124 / TASK-12402 — the leader-facing label is "Ikke indsendt", NOT "Kladde". A kladde is the
// employee's own working state and naming it here implies the leader can look inside one; from this
// side the only true statement is that nothing has arrived yet. The EMPLOYEE's own view
// (MyPeriods) still says "Kladde" — there it is accurate and it is their own draft.
type DisplayStatus = 'Indsendt' | 'Godkendt' | 'Afvist' | 'Ikke indsendt'

interface StatusMeta {
  label: DisplayStatus
  badgeClass: string
  /** Sort rank: Indsendt 0, Afvist 1, Godkendt 2, "Ikke indsendt" 3 (per the hifi). */
  rank: number
  /** A pending (leader-approvable) row → has Godkend/Afvis actions + selectable. */
  isPending: boolean
  /** A decided row (Godkendt/Afvist) — used for sort rank + display. */
  isDecided: boolean
  /** Reopen-eligible: APPROVED only (→ the Genåbn control, S89; gated on !payrollExported,
   *  S90). A REJECTED period is NOT reopenable — the backend reopen 409s it; the employee
   *  re-submits instead — so it shows no Genåbn (was a dead button pre-S91). */
  isReopenable: boolean
  isDraft: boolean
}

function statusMeta(status: TeamOverviewRow['status']): StatusMeta {
  switch (status) {
    case 'SUBMITTED':
    case 'EMPLOYEE_APPROVED':
      return { label: 'Indsendt', badgeClass: styles.badgeIndsendt, rank: 0, isPending: true, isDecided: false, isReopenable: false, isDraft: false }
    case 'APPROVED':
      return { label: 'Godkendt', badgeClass: styles.badgeGodkendt, rank: 2, isPending: false, isDecided: true, isReopenable: true, isDraft: false }
    case 'REJECTED':
      return { label: 'Afvist', badgeClass: styles.badgeAfvist, rank: 1, isPending: false, isDecided: true, isReopenable: false, isDraft: false }
    default:
      return { label: 'Ikke indsendt', badgeClass: styles.badgeKladde, rank: 3, isPending: false, isDecided: false, isReopenable: false, isDraft: true }
  }
}

// ── Danish number formatting (decimal comma, 1 dp) ───────────────────────────
function daNum(n: number, dec = 1): string {
  return Number(n).toFixed(dec).replace('.', ',')
}

/**
 * S124 / TASK-12402 — a withheld figure renders as an em dash, never as 0,0.
 *
 * `normRegistered` / `overtime` / `hasWarning` / `flexBalance` / `ferieUsed` all arrive NULL when
 * the employee has not sent the period — the owner rule is that a manager sees NOTHING before a
 * month is submitted — so the server omits them (see ApprovalEndpoints.cs). Every read here keys
 * off that NULL rather than re-deriving the status rule: one copy of the predicate, on the server,
 * is the point. "0,0 t" would claim the employee registered nothing, which is a different and
 * false statement. `normExpected` and `ferieTotal` survive as the honest denominators — both are
 * standing contract/quota facts that no registration can move.
 */
const WITHHELD = '—'
function daNumOrWithheld(n: number | null, dec = 1): string {
  return n === null ? WITHHELD : daNum(n, dec)
}

function flexText(flex: number): string {
  return (flex >= 0 ? '+' : '−') + daNum(Math.abs(flex)) + ' t'
}

function flexColorClass(flex: number): string {
  if (flex > 0.05) return styles.flexPositive
  if (flex < -0.05) return styles.flexNegative
  return styles.flexZero
}

/** da-DK long date, e.g. "29. marts 2026"; null-safe. */
function daDate(iso: string | null): string {
  if (!iso) return ''
  return new Date(iso).toLocaleDateString('da-DK', { day: 'numeric', month: 'long', year: 'numeric' })
}

type FilterKey = 'alle' | 'afventer' | 'godkendt' | 'advarsel'
type SortKey = 'navn' | 'status' | 'norm' | 'flex'
type SortDir = 'asc' | 'desc'

/** Per-row bulk outcome surfaced after a bulk-approve loop. */
type BulkOutcome = 'approved' | 'conflict'

// ── TeamRowDetail (the S88 expandable detail panel) ──────────────────────────
// Lazy by construction: this only mounts when its row is expanded, so the
// breakdown + compliance fetches fire on expand. NO SECOND SAVE PATH: the footer
// calls the PARENT's onApprove/onReject/onReopen props (= the page's status-aware
// handleApprove/openReject/handleReopen) — it never re-implements a mutation, never
// calls apiClient for writes. The reject Dialog state lives in the parent
// TeamOversigt.
interface TeamRowDetailProps {
  id: string
  row: TeamOverviewRow
  year: number
  month: number
  busy: boolean
  onApprove: (row: TeamOverviewRow) => void
  onReject: (row: TeamOverviewRow) => void
  onReopen: (row: TeamOverviewRow) => void
  /** S125 / TASK-12500 — the two collapsible sections. State is owned by the PAGE, not here, and
      that is the whole mechanism behind "session-sticky": this panel UNMOUNTS when a row collapses
      (the accordion keeps one row open), so per-row state would reset on every expand. Held one
      level up, a fold survives moving between employees and resets only on reload. */
  overblikOpen: boolean
  skemaOpen: boolean
  onToggleOverblik: () => void
  onToggleSkema: () => void
}

/** A collapsible section header inside the detail panel. A real <button> with `aria-expanded`, so it
    is keyboard- and screen-reader-operable rather than a clickable div. */
function DetailSection({
  title, open, onToggle, testId, children,
}: {
  title: string
  open: boolean
  onToggle: () => void
  testId: string
  children: ReactNode
}) {
  return (
    <section className={styles.detailSection}>
      <button
        type="button"
        className={styles.detailSectionHead}
        aria-expanded={open}
        onClick={onToggle}
        data-testid={testId}
      >
        <span className={`${styles.detailSectionCaret} ${open ? styles.detailSectionCaretOpen : ''}`}>▸</span>
        {title}
      </button>
      {open && children}
    </section>
  )
}

function TeamRowDetail({
  id, row, year, month, busy, onApprove, onReject, onReopen,
  overblikOpen, skemaOpen, onToggleOverblik, onToggleSkema,
}: TeamRowDetailProps) {
  const meta = statusMeta(row.status)
  // Lazy fetches — fire on mount (mount == expand).
  const { data: breakdown, loading: bdLoading, error: bdError } =
    useAllocationBreakdown(row.employeeId, year, month)
  // Per-employee fault-isolated: a failed compliance call sets `error` (it does
  // NOT throw) → we render a soft message and STILL render the rest of the panel.
  const { result: compliance, loading: compLoading, error: compError } =
    useCompliance(row.employeeId, year, month)

  const overtimeLabel = row.agreement === 'AC' ? 'Merarbejde' : 'Overarbejde'

  // Allocation bars — width relative to the max project hours (fallback: worked).
  const allocations = breakdown?.allocations ?? []
  const barBasis = Math.max(
    breakdown?.worked ?? 0,
    ...allocations.map(a => a.hours),
    0.0001,
  )

  const imbalance = breakdown?.hasAllocationImbalance ?? false
  const under = breakdown?.underAllocated ?? 0
  const over = breakdown?.overAllocated ?? 0

  // Compliance alerts — warnings + violations both surface as "Advarsel".
  const complianceMessages = compliance
    ? [...(compliance.warnings ?? []), ...(compliance.violations ?? [])].map(v => v.message)
    : []

  return (
    <td colSpan={9} className={styles.detailCell} id={id}>
      <div className={styles.detailInner}>
        {/* S125 / TASK-12500 — the summary row is ONE collapsible "Overblik" section. The old
            "SALDI" column label is retired: the section title carries the name, so keeping both
            would say it twice. Folding Overblik hides the balances AND the Fordeling split — they
            are one at-a-glance summary. */}
        <DetailSection
          title="Overblik"
          open={overblikOpen}
          onToggle={onToggleOverblik}
          testId={`toggle-overblik-${row.employeeId}`}
        >
        <div className={styles.detailColumns}>
          {/* Balances — reuses the row figures, NO extra fetch. */}
          <div className={styles.detailCol}>
            <div className={styles.saldiGrid}>
              <div className={styles.saldiCell}>
                <div className={styles.saldiCellLabel}>Flex saldo</div>
                <div className={`${styles.saldiCellValue} ${row.flexBalance === null ? '' : flexColorClass(row.flexBalance)}`}>
                  {row.flexBalance === null ? WITHHELD : flexText(row.flexBalance)}
                </div>
              </div>
              <div className={styles.saldiCell}>
                <div className={styles.saldiCellLabel}>Ferie</div>
                <div className={styles.saldiCellValue}>
                  {daNumOrWithheld(row.ferieUsed, 0)} / {daNum(row.ferieTotal, 0)} dage
                </div>
              </div>
              <div className={styles.saldiCell}>
                <div className={styles.saldiCellLabel}>Normtimer</div>
                <div className={styles.saldiCellValue}>
                  {daNumOrWithheld(row.normRegistered)} / {daNum(row.normExpected)} t
                </div>
              </div>
              <div className={styles.saldiCell}>
                <div className={styles.saldiCellLabel}>{overtimeLabel}</div>
                <div className={styles.saldiCellValue}>
                  {row.overtime === null ? WITHHELD : `${daNum(row.overtime)} t`}
                </div>
              </div>
            </div>
          </div>

          {/* Fordeling af arbejdstid — lazy breakdown. */}
          <div className={styles.detailCol}>
            <div className={styles.fordelingHead}>
              <span className={styles.detailLabel}>Fordeling af arbejdstid</span>
              {breakdown && (
                <span className={styles.fordelingSum}>
                  {daNum(breakdown.allocated)} / {daNum(breakdown.worked)} t
                </span>
              )}
            </div>
            {bdLoading ? (
              <div className={styles.detailMuted}>Henter fordeling…</div>
            ) : bdError ? (
              <div className={styles.detailMuted}>Kunne ikke hente fordeling</div>
            ) : breakdown ? (
              <div className={styles.fordelingList}>
                {allocations.length === 0 && (
                  <div className={styles.detailMuted}>Ingen fordeling registreret</div>
                )}
                {allocations.map(a => (
                  <div key={a.taskId} className={styles.allocEntry}>
                    <div className={styles.allocLine}>
                      <span className={styles.allocLabel}>{a.taskId}</span>
                      <span className={styles.allocValue}>{daNum(a.hours)} t</span>
                    </div>
                    <div className={styles.allocTrack}>
                      <div
                        className={styles.allocFill}
                        style={{ width: `${Math.min(100, (a.hours / barBasis) * 100)}%` }}
                      />
                    </div>
                  </div>
                ))}
                {/* Ikke fordelt — amber + bold ONLY when the per-day imbalance flags
                    (matches the row chip), muted otherwise. */}
                <div className={`${styles.allocEntry} ${imbalance ? styles.allocImbalance : ''}`}>
                  <div className={styles.allocLine}>
                    <span className={styles.allocLabel}>Ikke fordelt</span>
                    <span className={styles.allocValue}>{daNum(under)} t</span>
                  </div>
                  <div className={styles.allocTrack}>
                    <div
                      className={`${styles.allocFill} ${imbalance ? styles.allocFillImbalance : styles.allocFillMuted}`}
                      style={{ width: `${Math.min(100, (under / barBasis) * 100)}%` }}
                    />
                  </div>
                </div>
              </div>
            ) : null}
          </div>
        </div>
        </DetailSection>

        {/* Alerts — both allocation alerts gated behind hasAllocationImbalance so
            the detail never contradicts the row chip. */}
        {imbalance && under > 0 && (
          <div className={`${styles.detailAlert} ${styles.detailAlertWarn}`} role="status">
            {daNum(under)} t af {daNum(breakdown?.worked ?? 0)} t er ikke fordelt på projekter.
            Medarbejderen skal fordele hele sin registrerede tid.
          </div>
        )}
        {imbalance && over > 0 && (
          <div className={`${styles.detailAlert} ${styles.detailAlertWarn}`} role="status">
            {daNum(over)} t er fordelt på projekter ud over den registrerede tid.
          </div>
        )}
        {/* Advarsel — compliance, fault-isolated. */}
        {compError ? (
          <div className={`${styles.detailAlert} ${styles.detailAlertWarn}`} role="status">
            Advarsler kunne ikke hentes
          </div>
        ) : compLoading ? null : (
          complianceMessages.map((msg, i) => (
            <div key={i} className={`${styles.detailAlert} ${styles.detailAlertWarn}`} role="status">
              <strong>Advarsel:</strong> {msg}
            </div>
          ))
        )}
        {/* Begrundelse for afvisning. */}
        {row.status === 'REJECTED' && row.rejectionReason && (
          <div className={`${styles.detailAlert} ${styles.detailAlertError}`} role="status">
            <strong>Begrundelse for afvisning:</strong> {row.rejectionReason}
          </div>
        )}

        {/* S124 / TASK-12403 — the skema is the DEFAULT view: summary above, the full day-by-day
            grid below it, decision buttons after. Approving without seeing which days carried the
            hours is what this ordering prevents — the evidence sits between summary and verdict.
            S125 / TASK-12500 — now collapsible, still open by default. Read-only; the panel only
            renders for a month the employee actually sent. */}
        <DetailSection
          title="Skema"
          open={skemaOpen}
          onToggle={onToggleSkema}
          testId={`toggle-skema-${row.employeeId}`}
        >
          <ManagerSkemaGrid employeeId={row.employeeId} year={year} month={month} />
        </DetailSection>

        {/* Footer — status line + the large action buttons (parent handlers). */}
        <div className={styles.detailFooter}>
          <div className={styles.detailStatusLine}>
            {meta.isPending ? (
              `Indsendt ${daDate(row.submittedAt)} · lederfrist 10. ${formatMonthLabel(year, month).toLowerCase()}`
            ) : row.status === 'APPROVED' ? (
              `Godkendt ${daDate(row.decisionAt)}`
            ) : row.status === 'REJECTED' ? (
              `Afvist ${daDate(row.decisionAt)} · afventer ny indsendelse`
            ) : (
              // No "· kladde" suffix: from the leader's side there is nothing to characterise.
              'Ikke indsendt endnu'
            )}
          </div>
          <div className={styles.detailFooterActions}>
            {meta.isPending && row.periodId ? (
              <>
                <button
                  type="button"
                  className={styles.detailRejectBtn}
                  onClick={() => onReject(row)}
                  disabled={busy}
                >
                  Afvis måned
                </button>
                <button
                  type="button"
                  className={styles.detailApproveBtn}
                  onClick={() => onApprove(row)}
                  disabled={busy}
                >
                  Godkend måned
                </button>
              </>
            ) : meta.isReopenable && row.periodId && row.payrollExported ? (
              // S90 — the month is sent to lønkørsel: corrections-only, no reopen.
              <span className={styles.exportedBadge} title="Måneden er sendt til lønkørsel og kan ikke genåbnes">
                Sendt til lønkørsel
              </span>
            ) : meta.isReopenable && row.periodId ? (
              <button
                type="button"
                className={styles.detailReopenBtn}
                onClick={() => onReopen(row)}
                disabled={busy}
              >
                Genåbn måned
              </button>
            ) : null}
          </div>
        </div>
      </div>
    </td>
  )
}

export function TeamOversigt() {
  const { orgId } = useAuth()

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

  // Accordion: the expanded employeeId (one open at a time; null = all closed).
  const [expanded, setExpanded] = useState<string | null>(null)
  // S125 / TASK-12500 — the detail panel's two section folds. Deliberately PAGE-level, not per row:
  // the panel unmounts when a row collapses, so per-row state would reset on every expand. Here it
  // is SESSION-STICKY — fold Overblik once while reviewing twenty people and it stays folded — and
  // both default OPEN, so pressing an employee shows everything until you say otherwise.
  const [overblikOpen, setOverblikOpen] = useState(true)
  const [skemaOpen, setSkemaOpen] = useState(true)
  // Refs to the per-row toggle buttons so Escape can return focus to the toggle.
  const toggleRefs = useRef<Record<string, HTMLButtonElement | null>>({})

  // Reject dialog state
  const [rejectTarget, setRejectTarget] = useState<TeamOverviewRow | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [rejecting, setRejecting] = useState(false)

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
  // S124 / TASK-12402 — Norm-opfyldelse averages ONLY rows the employee actually sent. Averaging
  // over the whole team would re-leak the withheld hours in aggregate form (a single draft row's
  // registrations, recoverable from the team %), and would also drag the figure down with rows
  // that have no reportable number at all. Denominator = the rows counted, not the team size.
  // An all-draft team (every month starts that way) has NOTHING to average. Rendering 0% there
  // would state that the team registered nothing — the same fabricated-zero lie as "0,0 t".
  const normRows = rows.filter(r => r.normRegistered !== null)
  const kpiNorm: number | null = normRows.length > 0
    ? Math.round(normRows.reduce((s, r) => s + (r.normExpected > 0 ? r.normRegistered! / r.normExpected : 0), 0) / normRows.length * 100)
    : null

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
        // A withheld figure sorts BELOW every real ratio (-1), so un-submitted rows group at one
        // end deterministically instead of masquerading as 0% fulfilment.
        av = a.normRegistered === null ? -1 : a.normExpected > 0 ? a.normRegistered / a.normExpected : 0
        bv = b.normRegistered === null ? -1 : b.normExpected > 0 ? b.normRegistered / b.normExpected : 0
      } else {
        // Flex can legitimately be negative, so a withheld balance sorts at -Infinity rather than
        // at a magic number a real balance could collide with.
        av = a.flexBalance ?? Number.NEGATIVE_INFINITY
        bv = b.flexBalance ?? Number.NEGATIVE_INFINITY
      }
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

  // ── Accordion expand/collapse (one open at a time) ─────────────────────────
  const toggleExpand = useCallback((employeeId: string) => {
    setExpanded(prev => (prev === employeeId ? null : employeeId))
  }, [])

  // Escape collapses the open row and returns focus to its toggle button.
  useEffect(() => {
    if (!expanded) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        const id = expanded
        setExpanded(null)
        // Return focus to the toggle after the row collapses.
        toggleRefs.current[id]?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [expanded])

  // ── Status-aware single approve (mirrors ApprovalDashboard.tsx:230) ─────────
  // Single-shot: distinguishes 200 (ok) / 409 (lost race) / other. Returns the
  // outcome so the bulk loop can aggregate.
  const approveOne = useCallback(async (
    row: TeamOverviewRow,
  ): Promise<'approved' | 'conflict' | 'error'> => {
    if (!row.periodId) return 'error'
    // S116 typed switch (call-form verified) — the op binds NO request DTO;
    // neither form sends a body. Response derives `{periodId, status}`.
    const result = await apiClient.post('/api/approval/{periodId}/approve', {
      params: { path: { periodId: row.periodId } },
    })
    if (result.ok) return 'approved'
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
    // S116 typed switch (call-form verified) — body `{reason}` byte-identical.
    const result = await apiClient.post('/api/approval/{periodId}/reject', {
      params: { path: { periodId: rejectTarget.periodId } },
      body: { reason },
    })
    if (result.ok) {
      clearSelection(rejectTarget.employeeId)
      showToast(`${rejectTarget.displayName} afvist.`, 'success')
      closeReject()
      await refetch()
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

  // ── Reopen (leader+; S89 Phase 1 — was LocalHR+) ───────────────────────────
  const handleReopen = async (row: TeamOverviewRow) => {
    if (!row.periodId) return
    setBusyId(row.employeeId)
    try {
      // S116 typed switch (call-form verified) — body `{reason}` byte-identical.
      const result = await apiClient.post('/api/approval/{periodId}/reopen', {
        params: { path: { periodId: row.periodId } },
        body: { reason: 'Genåbnet af leder' },
      })
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

  // ── Bulk approve (FE loop of the hardened single-approve, sequential) ───────
  const handleBulkApprove = async () => {
    const targets = rows.filter(r => selected[r.employeeId] && statusMeta(r.status).isPending && r.periodId)
    if (targets.length === 0) return
    setBulkRunning(true)
    setBulkResults({})
    const results: Record<string, BulkOutcome> = {}
    const succeeded: string[] = []
    for (const row of targets) {
      // Sequential by design (same tree advisory lock) — do NOT parallelize.
      const outcome = await approveOne(row)
      if (outcome === 'approved') {
        results[row.employeeId] = 'approved'
        succeeded.push(row.employeeId)
      } else if (outcome === 'conflict') {
        results[row.employeeId] = 'conflict'
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
    if (parts.length > 0) {
      showToast(parts.join(' · '), conflictCount > 0 ? 'error' : 'success')
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
          {/* The tile changed meaning: it now covers only the rows that were actually sent, so it
              says so rather than silently averaging a different population than before. */}
          <p className={styles.kpiLabel}>
            Norm-opfyldelse
            {rows.length > 0 && (
              <span className={styles.kpiSuffix}> · {normRows.length} af {rows.length} indsendt</span>
            )}
          </p>
          <p className={styles.kpiValue} data-testid="kpi-norm-value">
            {kpiNorm === null ? WITHHELD : <>{kpiNorm}<span className={styles.kpiSuffix}>%</span></>}
          </p>
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
                // null ⇒ withheld (not sent): no ratio, so no progress bar — a bar at 0% would
                // read as "registered nothing this month".
                const ratio = row.normRegistered === null
                  ? null
                  : row.normExpected > 0 ? row.normRegistered / row.normExpected : 0
                const barColor = ratio === null
                  ? ''
                  : ratio >= 1 ? styles.barGreen : ratio >= 0.95 ? styles.barInfo : styles.barWarn
                const checked = !!selected[row.employeeId]
                const bulkOutcome = bulkResults[row.employeeId]
                // Expandable only when there is a sent period to inspect. Keyed on the withheld
                // marker, so it can never drift from what the server actually released.
                const canExpand = row.normRegistered !== null
                const isExpanded = canExpand && expanded === row.employeeId
                const detailId = `team-detail-${row.employeeId}`
                return (
                  <Fragment key={row.employeeId}>
                    {/* The whole-row click is gated on `canExpand` alongside the chevron. Left
                        ungated it was a dead affordance (pointer cursor + hover on a row that
                        cannot open — the S91 discipline) AND actively harmful: it set `expanded`
                        to an id that can never render, silently COLLAPSING whichever row was
                        open, and armed the Escape handler on an id with no toggleRef. */}
                    <tr
                      className={`${styles.bodyRow} ${canExpand ? styles.clickableRow : ''} ${checked || isExpanded ? styles.rowSelected : ''}`}
                      data-testid={`team-row-${row.employeeId}`}
                      onClick={canExpand ? () => toggleExpand(row.employeeId) : undefined}
                    >
                      {/* stopPropagation: checkbox cell must not toggle the row. */}
                      <td className={styles.checkboxCell} onClick={e => e.stopPropagation()}>
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => toggleOne(row.employeeId)}
                          disabled={!meta.isPending || !row.periodId}
                          aria-label={`Vælg ${row.displayName}`}
                        />
                      </td>
                      <td>
                        {/* S124 / TASK-12402 — NO expander on a row the employee has not sent. The
                            detail panel's whole purpose is the un-submitted content: withheld
                            Normtimer/overtid, plus a LAZY fetch of "Fordeling af arbejdstid" (the
                            per-task hour split) and the compliance warnings. Rendering a chevron
                            that opens a panel with nothing legitimate in it would also fire two
                            requests per row for data the manager may not read. */}
                        {canExpand ? (
                          <button
                            type="button"
                            ref={el => { toggleRefs.current[row.employeeId] = el }}
                            className={styles.chevronBtn}
                            aria-expanded={isExpanded}
                            aria-controls={isExpanded ? detailId : undefined}
                            aria-label={`${isExpanded ? 'Skjul' : 'Vis'} detaljer for ${row.displayName}`}
                            onClick={e => { e.stopPropagation(); toggleExpand(row.employeeId) }}
                          >
                            <span className={`${styles.chevron} ${isExpanded ? styles.chevronOpen : ''}`}>▸</span>
                          </button>
                        ) : (
                          <span className={styles.chevronBtn} aria-hidden="true" />
                        )}
                        <span className={styles.empName}>{row.displayName}</span>
                        <span className={styles.empId}>{row.employeeId}</span>
                      </td>
                      <td className={styles.secondary}>{row.agreement}</td>
                      <td>
                        <span className={`${styles.badge} ${meta.badgeClass}`}>{meta.label}</span>
                      </td>
                      <td className={styles.nowrap}>
                        <div>{daNumOrWithheld(row.normRegistered)} / {daNum(row.normExpected)} t</div>
                        {ratio !== null && (
                          <div className={styles.barTrack}>
                            <div
                              className={`${styles.barFill} ${barColor}`}
                              style={{ width: `${Math.min(100, Math.round(ratio * 100))}%` }}
                            />
                          </div>
                        )}
                      </td>
                      <td className={`${styles.right} ${styles.flexCell} ${row.flexBalance === null ? '' : flexColorClass(row.flexBalance)}`}>
                        {row.flexBalance === null ? WITHHELD : flexText(row.flexBalance)}
                      </td>
                      {/* The QUOTA stays (a standing entitlement, the honest denominator); only the
                          USED count is withheld — it moves the moment the employee drafts a day off. */}
                      <td className={styles.secondary}>
                        {daNumOrWithheld(row.ferieUsed, 0)} / {daNum(row.ferieTotal, 0)} dage
                      </td>
                      <td>
                        {row.hasWarning ? (
                          <span className={styles.warnChip} title="Manglende fordeling på projekter">
                            Manglende fordeling
                          </span>
                        ) : (
                          <span className={styles.emDash}>—</span>
                        )}
                      </td>
                      {/* stopPropagation: Handling cell must not toggle the row. */}
                      <td className={styles.handlingCell} onClick={e => e.stopPropagation()}>
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
                        ) : meta.isReopenable && row.periodId && row.payrollExported ? (
                          // S90 — the month is sent to lønkørsel: corrections-only, no reopen.
                          <span className={styles.exportedBadge} title="Måneden er sendt til lønkørsel og kan ikke genåbnes">
                            Sendt til lønkørsel
                          </span>
                        ) : meta.isReopenable && row.periodId ? (
                          <button
                            type="button"
                            className={styles.reopenBtn}
                            onClick={() => handleReopen(row)}
                            disabled={busyId === row.employeeId}
                          >
                            Genåbn
                          </button>
                        ) : row.status === 'REJECTED' && row.periodId ? (
                          // S91 — a rejected month is not reopenable (the employee re-submits);
                          // no dead Genåbn button. The Status column already shows "Afvist".
                          <span className={styles.notSubmitted}>Afventer ny indsendelse</span>
                        ) : (
                          // S124 / TASK-12402 — the Status badge now READS "Ikke indsendt", so
                          // repeating it here said the same thing twice across two columns. There is
                          // no action to offer either, so the Handling cell is simply empty.
                          <span className={styles.notSubmitted} aria-hidden="true">{WITHHELD}</span>
                        )}
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr className={styles.detailRow} data-testid={`team-detail-row-${row.employeeId}`}>
                        <TeamRowDetail
                          id={detailId}
                          row={row}
                          year={year}
                          month={month}
                          busy={busyId === row.employeeId || bulkRunning}
                          onApprove={handleApprove}
                          onReject={openReject}
                          onReopen={handleReopen}
                          overblikOpen={overblikOpen}
                          skemaOpen={skemaOpen}
                          onToggleOverblik={() => setOverblikOpen(o => !o)}
                          onToggleSkema={() => setSkemaOpen(o => !o)}
                        />
                      </tr>
                    )}
                  </Fragment>
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
    </div>
  )
}
