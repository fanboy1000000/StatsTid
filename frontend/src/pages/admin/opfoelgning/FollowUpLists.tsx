// SPRINT-140 / TASK-14006 (refinement B4) — the nine READ-ONLY process lists
// behind the HR follow-up landing page's tiles (the tenth, the backdate
// worklist, is `WorklistList.tsx` from TASK-14007 and is not touched here).
//
// EVERY LIST HERE IS READ-ONLY (owner ruling OQ-4, 2026-09-08): none of the
// process actions these lists point at — reconcile a payout, resolve a
// flagged settlement, record a §21 transfer agreement, the settlement
// reversal a SETTLED_YEAR worklist row names, or record a §26 payout
// request — has a frontend today. Each such list says so in an "action note"
// rather than offering a form; the four ruled by OQ-4 name S141 explicitly,
// the §26 request (not named by OQ-4 but equally API-only, per the register's
// HRP-007 row) is worded without promising a sprint. Three lists whose
// underlying action ALREADY has a frontend elsewhere in the product (approve/
// reject/reopen in Teamoversigt; assign an approver or renew delegation cover
// on the merged admin page) point there instead of inventing a duplicate
// control here.
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Alert, Badge, Button, Spinner, Table, Textarea } from '../../../components/ui'
import { useLeaverMonthActions } from '../../../hooks/useHrFollowUp'
import { ManagerSkemaGrid } from '../../approval/ManagerSkemaGrid'
import { formatMonthLabel } from '../../../lib/locale'
import { daysLabel, formatDaDate, formatDaLongDate } from './followUpFormat'
import type {
  HrPastDeadlineResponse,
  HrLeaverFinalMonthResponse,
  HrApprovedNotExportedResponse,
  HrUncoveredApproversResponse,
  HrCannotRegisterResponse,
  PendingSettlementReviewListResponse,
  TerminationPayoutUnrequestedListResponse,
  VacationTransferAgreementNeededListResponse,
  PayoutPendingListResponse,
  HrFollowUpMonthItem,
} from '../../../hooks/useHrFollowUp'
import styles from './FollowUpLists.module.css'

// ── shared shell ────────────────────────────────────────────────────────────

interface ListShellProps {
  testId: string
  heading: string
  subheading?: string
  loading: boolean
  error: string | null
  isEmpty: boolean
  emptyText: string
  /** The rolling-12-month lookback floor caption (the three history tiles). */
  floorNote?: string
  /** The eventual-consistency lag note (the two event-backed lists). */
  lagNote?: string
  /** "Handled via API today" / "use this existing screen instead" — never a form. */
  actionNote?: ReactNode
  children?: ReactNode
}

function ListShell({
  testId, heading, subheading, loading, error, isEmpty, emptyText, floorNote, lagNote, actionNote, children,
}: ListShellProps) {
  return (
    <div className={styles.root} data-testid={testId}>
      <h2 className={styles.heading}>{heading}</h2>
      {subheading && <p className={styles.subheading}>{subheading}</p>}
      {floorNote && <p className={styles.floorNote} data-testid={`${testId}-floor-note`}>{floorNote}</p>}
      {lagNote && <p className={styles.lagNote} data-testid={`${testId}-lag-note`}>{lagNote}</p>}
      {actionNote && <div className={styles.actionNote} data-testid={`${testId}-action-note`}>{actionNote}</div>}
      {loading && (
        <div className={styles.spinner}>
          <Spinner size="lg" />
        </div>
      )}
      {!loading && error && (
        <div data-testid={`${testId}-error`}>
          <Alert variant="error">{error}</Alert>
        </div>
      )}
      {!loading && !error && isEmpty && (
        <div className={styles.empty} data-testid={`${testId}-empty`}>{emptyText}</div>
      )}
      {!loading && !error && !isEmpty && children}
    </div>
  )
}

const HANDLED_VIA_API_S141 = (process: string) => (
  <>{process} håndteres via API i dag; skærm følger i S141.</>
)

// ── month-item rendering (shared by past-deadline / leaver-final-month / approved-not-exported) ──

const PERIOD_STATUS_LABEL: Record<HrFollowUpMonthItem['periodStatus'], string> = {
  NONE: 'Ikke oprettet',
  DRAFT: 'Kladde',
  EMPLOYEE_APPROVED: 'Medarbejder godkendt',
  SUBMITTED: 'Indsendt',
  APPROVED: 'Godkendt',
  REJECTED: 'Afvist',
}

function MonthItemsTable({ items, testId }: { items: HrFollowUpMonthItem[]; testId: string }) {
  return (
    <Table headers={['Medarbejder', 'Måned', 'Status', 'Frist overskredet med', 'Fristkilde']}>
      {items.map((item) => (
        <tr key={`${item.employeeId}-${item.year}-${item.month}`} data-testid={`${testId}-row-${item.employeeId}-${item.year}-${item.month}`}>
          <td>{item.displayName}</td>
          <td>{formatMonthLabel(item.year, item.month)}</td>
          <td>{PERIOD_STATUS_LABEL[item.periodStatus]}</td>
          <td>{item.daysPastAnchor > 0 ? daysLabel(item.daysPastAnchor) : '—'}</td>
          <td>
            {item.deadlineSource === 'computed' ? (
              <span className={styles.computedBadge} data-testid={`${testId}-computed-${item.employeeId}-${item.year}-${item.month}`}>
                beregnet (ingen gemt frist)
              </span>
            ) : (
              'gemt'
            )}
          </td>
        </tr>
      ))}
    </Table>
  )
}

// ── Past deadline (HRP-012) ──────────────────────────────────────────────────

export function PastDeadlineList({
  data, loading, error,
}: { data: HrPastDeadlineResponse | null; loading: boolean; error: string | null }) {
  const employeeLate = data?.employeeLate ?? []
  const approverLate = data?.approverLate ?? []
  const isEmpty = employeeLate.length === 0 && approverLate.length === 0

  return (
    <ListShell
      testId="list-past-deadline"
      heading="Forbi frist"
      subheading="Måneder hvor medarbejderens eller godkenderens frist er overskredet."
      loading={loading}
      error={error}
      isEmpty={isEmpty}
      emptyText="Ingen måneder forbi frist."
      floorNote={data ? `Ser 12 måneder tilbage (fra ${data.lookbackFloor}).` : undefined}
      actionNote={
        <>
          Godkend, afvis eller genåbn perioden i <Link to="/godkend/oversigt">Teamoversigt</Link>.
        </>
      }
    >
      <div className={styles.subsection}>
        <h3 className={styles.subsectionHeading}>Medarbejder forsinket ({data?.employeeLateCount ?? 0})</h3>
        {employeeLate.length > 0 ? (
          <MonthItemsTable items={employeeLate} testId="list-past-deadline-employee" />
        ) : (
          <p className={styles.empty}>Ingen.</p>
        )}
      </div>
      <div className={styles.subsection}>
        <h3 className={styles.subsectionHeading}>Godkender forsinket ({data?.approverLateCount ?? 0})</h3>
        {approverLate.length > 0 ? (
          <MonthItemsTable items={approverLate} testId="list-past-deadline-approver" />
        ) : (
          <p className={styles.empty}>Ingen.</p>
        )}
      </div>
      <p className={styles.caveat} data-testid="list-past-deadline-caveat">
        En fratrådt medarbejders sidste måned kan optræde her OG på "Sidste måned ved fratrædelse" —
        de to lister hører til forskellige roller, og tallene må ikke lægges sammen.
      </p>
    </ListShell>
  )
}

// ── Leaver's final month: the row's actions (S147 / TASK-14714, HRP-011) ─────

/** A refusal in Danish. `apiClient` hands a failure back as { status, error } where `error` is
    the RAW response text, so it is parsed here (never rendered raw). The four-eyes 403 carries an
    English `reason` (`ApprovalSenderGuard.DenialReason`); the 422 `month_outside_employment_period`
    carries a Danish `message`; the send's coverage 422 carries `missingDays`; a 409 names the
    month's status in its `error` sentence. Anything else gets a plain Danish sentence. */
export function leaverRefusalText(status: number, rawError: string | undefined): string {
  let b: { error?: unknown; reason?: unknown; message?: unknown; missingDays?: unknown } = {}
  try {
    const parsed: unknown = JSON.parse(rawError ?? '')
    if (parsed && typeof parsed === 'object') b = parsed as typeof b
  } catch {
    // not JSON - fall through to the generic sentences
  }
  if (status === 403) {
    const reason = typeof b.reason === 'string' ? b.reason : ''
    if (reason.includes('four-eyes')) {
      return 'Du har selv sendt denne måned, så en anden HR-medarbejder skal godkende eller afvise den (fire-øjne-princippet).'
    }
    return 'Du har ikke adgang til at udføre handlingen på denne måned.'
  }
  if (status === 422) {
    if (typeof b.message === 'string' && b.message) return b.message
    if (Array.isArray(b.missingDays) && b.missingDays.length > 0) {
      const days = (b.missingDays as unknown[]).filter((d): d is string => typeof d === 'string').map(formatDaDate).join(', ')
      return `Måneden kan ikke sendes endnu: der mangler registreringer for ${days}.`
    }
    return 'Måneden kan ikke sendes endnu: der mangler registreringer eller fordeling. Åbn måneden for at se detaljerne.'
  }
  if (status === 409) {
    const m = typeof b.error === 'string' ? /status (\w+)/.exec(b.error) : null
    const label = m ? PERIOD_STATUS_LABEL[m[1] as HrFollowUpMonthItem['periodStatus']] : undefined
    return label
      ? `Måneden er allerede i status "${label}". Genindlæs listen.`
      : 'Måneden har en anden status, end listen viser. Genindlæs listen.'
  }
  return 'Handlingen mislykkedes. Prøv igen.'
}

/** "Leder til og med 18. marts 2026, kun HR fra 19. marts 2026". A row the server did not
    date says nothing rather than guess. */
export function leaverTurnText(item: HrFollowUpMonthItem): string | null {
  if (!item.lastManagerDay || !item.firstHrOnlyDay) return null
  return `Leder til og med ${formatDaLongDate(item.lastManagerDay)}, kun HR fra ${formatDaLongDate(item.firstHrOnlyDay)}`
}

type RowOutcome = { kind: 'success' | 'error'; text: string } | null

function LeaverMonthRow({ item, testId }: { item: HrFollowUpMonthItem; testId: string }) {
  const actions = useLeaverMonthActions()
  const key = `${item.employeeId}-${item.year}-${item.month}`
  const [periodId, setPeriodId] = useState<string | null>(item.periodId)
  const [status, setStatus] = useState(item.periodStatus)
  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<RowOutcome>(null)
  const [monthOpen, setMonthOpen] = useState(false)
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')

  const refuse = (r: { status: number; error: string }) =>
    setOutcome({ kind: 'error', text: leaverRefusalText(r.status, r.error) })

  const send = async () => {
    setBusy(true)
    setOutcome(null)
    const r = await actions.sendMonth(item.employeeId, item.year, item.month)
    setBusy(false)
    if (r.ok) {
      setPeriodId(r.data.periodId)
      setStatus(r.data.status)
      setOutcome({ kind: 'success', text: `${item.displayName}: måneden er sendt.` })
    } else refuse(r)
  }

  const approve = async () => {
    if (!periodId) return
    setBusy(true)
    setOutcome(null)
    const r = await actions.approvePeriod(periodId)
    setBusy(false)
    if (r.ok) {
      setStatus('APPROVED')
      setOutcome({ kind: 'success', text: `${item.displayName}: måneden er godkendt.` })
    } else refuse(r)
  }

  const reject = async () => {
    if (!periodId) return
    setBusy(true)
    setOutcome(null)
    const r = await actions.rejectPeriod(periodId, reason.trim())
    setBusy(false)
    if (r.ok) {
      setStatus('REJECTED')
      setRejecting(false)
      setReason('')
      setOutcome({ kind: 'success', text: `${item.displayName}: måneden er afvist.` })
    } else refuse(r)
  }

  const turn = leaverTurnText(item)
  const canSend = periodId === null || status === 'NONE' || status === 'DRAFT' || status === 'REJECTED'
  const canDecide = periodId !== null && (status === 'SUBMITTED' || status === 'EMPLOYEE_APPROVED')

  return (
    <>
    <tr data-testid={`${testId}-row-${key}`}>
      <td>{item.displayName}</td>
      <td>{formatMonthLabel(item.year, item.month)}</td>
      <td data-testid={`${testId}-status-${key}`}>{PERIOD_STATUS_LABEL[status]}</td>
      <td>{item.daysPastAnchor > 0 ? daysLabel(item.daysPastAnchor) : '—'}</td>
      <td>
        {item.deadlineSource === 'computed' ? (
          <span className={styles.computedBadge} data-testid={`${testId}-computed-${key}`}>
            beregnet (ingen gemt frist)
          </span>
        ) : (
          'gemt'
        )}
      </td>
      <td data-testid={`${testId}-turn-${key}`}>{turn ?? '—'}</td>
      <td>
        <div className={styles.rowActions}>
          <Button
            size="sm"
            variant="secondary"
            aria-expanded={monthOpen}
            aria-controls={`${testId}-grid-${key}`}
            onClick={() => setMonthOpen((v) => !v)}
            data-testid={`${testId}-open-${key}`}
          >
            {monthOpen ? 'Skjul måned' : 'Åbn måned'}
          </Button>
          {canSend && (
            <Button size="sm" disabled={busy} onClick={send} data-testid={`${testId}-send-${key}`}>
              Send måned
            </Button>
          )}
          {canDecide && (
            <>
              <Button size="sm" disabled={busy} onClick={approve} data-testid={`${testId}-approve-${key}`}>
                Godkend
              </Button>
              <Button
                size="sm"
                variant="danger"
                disabled={busy}
                aria-expanded={rejecting}
                aria-controls={`${testId}-reason-${key}`}
                onClick={() => {
                  const next = !rejecting
                  setRejecting(next)
                  if (next) setTimeout(() => document.getElementById(`${testId}-reason-${key}`)?.focus(), 0)
                }}
                data-testid={`${testId}-reject-${key}`}
              >
                Afvis
              </Button>
            </>
          )}
        </div>
        {rejecting && canDecide && (
          <div className={styles.rejectBox}>
            <Textarea
              id={`${testId}-reason-${key}`}
              rows={2}
              placeholder="Begrundelse (valgfri)"
              aria-label={`Begrundelse for afvisning af ${item.displayName}`}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
            />
            <Button size="sm" variant="danger" disabled={busy} onClick={reject} data-testid={`${testId}-reject-confirm-${key}`}>
              Bekræft afvisning
            </Button>
          </div>
        )}
        {outcome && (
          <div data-testid={`${testId}-outcome-${key}`}>
            <Alert variant={outcome.kind === 'success' ? 'success' : 'error'}>{outcome.text}</Alert>
          </div>
        )}
      </td>
    </tr>
    {monthOpen && (
      <tr>
        <td colSpan={7} id={`${testId}-grid-${key}`} data-testid={`${testId}-grid-${key}`}>
          <ManagerSkemaGrid employeeId={item.employeeId} year={item.year} month={item.month} />
        </td>
      </tr>
    )}
    </>
  )
}

function LeaverMonthsTable({ items, testId }: { items: HrFollowUpMonthItem[]; testId: string }) {
  return (
    <Table headers={['Medarbejder', 'Måned', 'Status', 'Frist overskredet med', 'Fristkilde', 'Hvis tur', 'Handlinger']}>
      {items.map((item) => (
        <LeaverMonthRow key={`${item.employeeId}-${item.year}-${item.month}`} item={item} testId={testId} />
      ))}
    </Table>
  )
}

// ── Leaver's final month (HRP-011) ──────────────────────────────────────────

export function LeaverFinalMonthList({
  data, loading, error,
}: { data: HrLeaverFinalMonthResponse | null; loading: boolean; error: string | null }) {
  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-leaver-final-month"
      heading="Sidste måned ved fratrædelse"
      subheading="Fratrådte medarbejderes sidste ansættelsesmåned, som mangler at blive godkendt."
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen fratrådte med en åben sidste måned."
      floorNote={data ? `Ser 12 måneder tilbage (fra ${data.lookbackFloor}).` : undefined}
      actionNote="Fra dag 6 efter fratrædelsen er det kun HR, der kan godkende, afvise eller genåbne måneden. Handlingerne ligger på hver række."
    >
      <LeaverMonthsTable items={items} testId="list-leaver-final-month" />
      <p className={styles.caveat} data-testid="list-leaver-final-month-caveat">
        En fratrådt medarbejders sidste måned kan optræde her OG under "Forbi frist" — de to lister
        hører til forskellige roller, og tallene må ikke lægges sammen.
      </p>
    </ListShell>
  )
}

// ── Approved, not exported (HRP-022) ─────────────────────────────────────────

export function ApprovedNotExportedList({
  data, loading, error,
}: { data: HrApprovedNotExportedResponse | null; loading: boolean; error: string | null }) {
  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-approved-not-exported"
      heading="Godkendt, ikke eksporteret"
      subheading="Godkendte måneder uden en tilhørende lønkørsel."
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen godkendte måneder mangler eksport."
      floorNote={data ? `Ser 12 måneder tilbage (fra ${data.lookbackFloor}).` : undefined}
      actionNote="Eksport til løn sker i dag via Lønadministrationens API — ingen skærm kalder den endnu."
    >
      <MonthItemsTable items={items} testId="list-approved-not-exported" />
    </ListShell>
  )
}

// ── Uncovered approvers (HRP-013/014) ────────────────────────────────────────

export function UncoveredApproversList({
  data, loading, error,
}: { data: HrUncoveredApproversResponse | null; loading: boolean; error: string | null }) {
  const orphans = data?.orphans ?? []
  const expired = data?.expiredDelegations ?? []
  const isEmpty = orphans.length === 0 && expired.length === 0

  return (
    <ListShell
      testId="list-uncovered-approvers"
      heading="Uden godkenderdækning"
      subheading="Medarbejdere uden strukturel godkender, og udløbne stedfortræder-delegeringer (seneste 30 dage)."
      loading={loading}
      error={error}
      isEmpty={isEmpty}
      emptyText="Ingen uden godkenderdækning."
      lagNote={data?.eventSourceLagNote}
      actionNote={
        <>
          Tildel godkender under <Link to="/admin/organisation-medarbejdere">Organisation &amp; medarbejdere</Link>;
          forny stedfortræder-dækning under <Link to="/godkend/vikariering">Vikariering</Link>.
        </>
      }
    >
      <div className={styles.subsection}>
        <h3 className={styles.subsectionHeading}>Forældreløse medarbejdere ({data?.orphanCount ?? 0})</h3>
        {orphans.length > 0 ? (
          <Table headers={['Medarbejder', 'Enhed']}>
            {orphans.map((o) => (
              <tr key={o.employeeId} data-testid={`list-uncovered-approvers-orphan-${o.employeeId}`}>
                <td>{o.displayName}</td>
                <td>{o.unitName ?? '—'}</td>
              </tr>
            ))}
          </Table>
        ) : (
          <p className={styles.empty}>Ingen.</p>
        )}
      </div>
      <div className={styles.subsection}>
        <h3 className={styles.subsectionHeading}>
          Udløbne delegeringer ({data?.expiredDelegationCount ?? 0}, heraf {data?.expiredWithoutActiveCoverCount ?? 0} uden aktiv dækning)
        </h3>
        {expired.length > 0 ? (
          <Table headers={['Fraværende leder', 'Stedfortræder', 'Udløbet', 'Dage siden', 'Aktiv dækning?']}>
            {expired.map((d) => (
              <tr key={d.vikarId} data-testid={`list-uncovered-approvers-expired-${d.vikarId}`}>
                <td>{d.absentApproverName}</td>
                <td>{d.vikarUserName ?? d.vikarUserId}</td>
                <td>{d.expiredAt}</td>
                <td>{daysLabel(d.daysSinceExpiry)}</td>
                <td data-testid={`list-uncovered-approvers-cover-${d.vikarId}`}>{d.approverHasActiveCover ? 'Ja' : 'Nej'}</td>
              </tr>
            ))}
          </Table>
        ) : (
          <p className={styles.empty}>Ingen.</p>
        )}
      </div>
    </ListShell>
  )
}

// ── Employees who cannot register (HRP-015) ──────────────────────────────────

// The backend's `missingRecord` is one of `HrMissingEmploymentRecordKinds`
// (AGREEMENT_CODE / EMPLOYMENT_PROFILE / BOTH — S141 / refinement B8). The
// generated contract types this as a plain `string`, not a literal union
// (unlike e.g. `source`/`trigger`/`settlementState` elsewhere in this file,
// which ARE backed by real C# enums and so arrive as string-literal unions)
// — so this map falls back to the raw value for anything it doesn't
// recognise rather than silently mislabeling it.
const MISSING_RECORD_LABEL: Record<string, string> = {
  AGREEMENT_CODE: 'Overenskomstkode',
  EMPLOYMENT_PROFILE: 'Ansættelsesprofil',
  BOTH: 'Overenskomstkode og ansættelsesprofil',
}

export function CannotRegisterList({
  data, loading, error,
}: { data: HrCannotRegisterResponse | null; loading: boolean; error: string | null }) {
  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-cannot-register"
      heading="Kan ikke registrere tid"
      subheading="Medarbejdere uden en overenskomstkode eller ansættelsesprofil, der dækker i dag."
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen medarbejdere mangler en dækkende overenskomstkode eller ansættelsesprofil."
      actionNote={
        <>
          Ret den manglende overenskomstkode eller ansættelsesprofil under{' '}
          <Link to="/admin/organisation-medarbejdere">Organisation &amp; medarbejdere</Link> — medmindre
          rækkens status viser en planlagt dækning: så retter dækningen sig selv, og der skal ikke gøres noget.
        </>
      }
    >
      <Table headers={['Medarbejder', 'Enhed', 'Manglende registrering', 'Mangler dækning siden', 'Dage', 'Status']}>
        {items.map((item) => (
          <tr key={item.employeeId} data-testid={`list-cannot-register-row-${item.employeeId}`}>
            <td>{item.displayName}</td>
            <td>{item.unitName ?? '—'}</td>
            <td data-testid={`list-cannot-register-missing-${item.employeeId}`}>
              {MISSING_RECORD_LABEL[item.missingRecord] ?? item.missingRecord}
            </td>
            <td>{item.gapSince ?? '—'}</td>
            <td>{item.daysSinceGapStart !== null ? daysLabel(item.daysSinceGapStart) : '—'}</td>
            <td data-testid={`list-cannot-register-status-${item.employeeId}`}>
              {item.coveredFrom ? (
                <Badge variant="success">Planlagt fra {formatDaLongDate(item.coveredFrom)}</Badge>
              ) : (
                <Badge variant="warning">Skal rettes</Badge>
              )}
            </td>
          </tr>
        ))}
      </Table>
    </ListShell>
  )
}

// ── Settlement reviews (HRP-005 / 005b) ──────────────────────────────────────

export function SettlementReviewsList({
  data, loading, error,
}: { data: PendingSettlementReviewListResponse | null; loading: boolean; error: string | null }) {
  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-settlement-reviews"
      heading="Afregninger til gennemgang"
      subheading="Ventende manuel gennemgang (PENDING_REVIEW) og konfliktmarkerede afregninger fra en afvist fratrædelse."
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen afregninger venter på gennemgang."
      lagNote={data?.eventSourceLagNote}
      actionNote={HANDLED_VIA_API_S141('Gennemgang af en markeret afregning')}
    >
      <Table headers={['Medarbejder', 'Type/år', 'Kilde', 'Udløser', 'Markerede dage', 'Alder']}>
        {items.map((item) => (
          <tr
            key={`${item.employeeId}-${item.entitlementType}-${item.entitlementYear}-${item.settlementSequence}`}
            data-testid={`list-settlement-reviews-row-${item.employeeId}-${item.entitlementYear}`}
          >
            <td>{item.employeeId}</td>
            <td>{item.entitlementType} {item.entitlementYear}</td>
            <td>
              <Badge variant={item.source === 'event' ? 'warning' : 'default'}>
                {item.source === 'event' ? 'Konflikt (event)' : 'Ventende gennemgang'}
              </Badge>
            </td>
            <td>{item.trigger === 'TERMINATION' ? 'Fratrædelse' : 'Årsskifte'}</td>
            <td>{item.flaggedDays}</td>
            <td>{daysLabel(item.ageDays)}</td>
          </tr>
        ))}
      </Table>
    </ListShell>
  )
}

// ── §26 payouts outstanding (HRP-007) ────────────────────────────────────────

export function TerminationPayoutsList({
  data, loading, error,
}: { data: TerminationPayoutUnrequestedListResponse | null; loading: boolean; error: string | null }) {
  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-termination-payouts"
      heading="§26-udbetalinger uden anmodning"
      subheading="Afsluttede fratrædelses-afregninger med krystalliserede dage, hvor ingen §26-anmodning er registreret endnu."
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen fratrædelser mangler en §26-anmodning."
      actionNote="Registrering af en §26-anmodning sker i dag via API'et — ingen skærm her endnu."
    >
      <Table headers={['Medarbejder', 'Type/år', 'Krystalliserede dage', 'Alder']}>
        {items.map((item) => (
          <tr
            key={`${item.employeeId}-${item.entitlementType}-${item.entitlementYear}-${item.settlementSequence}`}
            data-testid={`list-termination-payouts-row-${item.employeeId}-${item.entitlementYear}`}
          >
            <td>{item.employeeId}</td>
            <td>{item.entitlementType} {item.entitlementYear}</td>
            <td>{item.crystallizedDays}</td>
            <td>{daysLabel(item.ageDays)}</td>
          </tr>
        ))}
      </Table>
    </ListShell>
  )
}

// ── §21 fifth-week transfer agreements (HRP-010) ─────────────────────────────

export function TransferAgreementsList({
  data, loading, error,
}: { data: VacationTransferAgreementNeededListResponse | null; loading: boolean; error: string | null }) {
  if (!loading && !error && data && !data.windowOpen) {
    return (
      <ListShell
        testId="list-transfer-agreements"
        heading="§21 — femte ferieuge"
        loading={false}
        error={null}
        isEmpty={false}
        emptyText=""
      >
        <p data-testid="list-transfer-agreements-closed">
          Vinduet er lukket. Muligheden for at aftale overførsel af femte ferieuge åbner{' '}
          {formatDaLongDate(data.windowOpensOn)} og lukker {formatDaLongDate(data.deadline)}.
        </p>
      </ListShell>
    )
  }

  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-transfer-agreements"
      heading="§21 — femte ferieuge"
      subheading={data ? `${data.daysToDeadline} dage til fristen (${data.deadline}).` : undefined}
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen medarbejdere mangler en §21-aftale."
      actionNote="Registrering af en §21-aftale sker i dag via API'et; skærm følger i S141."
    >
      {data && (
        <p className={styles.caveat} data-testid="list-transfer-agreements-projection-note">
          {data.projectionNote}
        </p>
      )}
      <Table headers={['Medarbejder', 'Under loft (dage)', 'Maks. overførsel', 'Frist']}>
        {items.map((item) => (
          <tr key={item.employeeId} data-testid={`list-transfer-agreements-row-${item.employeeId}`}>
            <td>{item.employeeId}</td>
            <td>{item.underCapDays}</td>
            <td>{item.carryoverMax}</td>
            <td>{item.deadline}</td>
          </tr>
        ))}
      </Table>
      {data && data.cannotComputeCount > 0 && (
        <Alert variant="warning">
          <span data-testid="list-transfer-agreements-cannot-compute">
            {data.cannotComputeCount} medarbejder(e) kunne ikke beregnes og indgår ikke i listen ovenfor.
          </span>
        </Alert>
      )}
    </ListShell>
  )
}

// ── Payout pending (HRP-006, pre-existing) ───────────────────────────────────

export function PayoutPendingList({
  data, loading, error,
}: { data: PayoutPendingListResponse | null; loading: boolean; error: string | null }) {
  const items = data?.items ?? []
  return (
    <ListShell
      testId="list-payout-pending"
      heading="Udbetaling afventer afstemning"
      subheading="Afregnede udbetalingsdage, der endnu ikke er afstemt med lønkørslen."
      loading={loading}
      error={error}
      isEmpty={items.length === 0}
      emptyText="Ingen udbetalinger afventer afstemning."
      actionNote={HANDLED_VIA_API_S141('Afstemning ("reconcile") af en udbetaling')}
    >
      <Table headers={['Medarbejder', 'Type/år', 'Dage', 'Afregnet']}>
        {items.map((item) => (
          <tr
            key={`${item.employeeId}-${item.entitlementType}-${item.entitlementYear}-${item.sequence}`}
            data-testid={`list-payout-pending-row-${item.employeeId}-${item.entitlementYear}`}
          >
            <td>{item.employeeId}</td>
            <td>{item.entitlementType} {item.entitlementYear}</td>
            <td>{item.payoutDays}</td>
            <td>{formatDaDate(item.settledAt)}</td>
          </tr>
        ))}
      </Table>
    </ListShell>
  )
}

