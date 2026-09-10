// SPRINT-140 / TASK-14006 (refinement B4) — the HR follow-up landing page.
// S139 catalogued fifteen processes the system hands HR and then forgets;
// nine had no screen at all, one was write-only. The owner ruled the shape
// (S139 "Three shapes for Increment 4", shape 3): ONE landing page, ONE tile
// per process — an open count and the age of the oldest item — with each
// process's own list behind its tile, on the SAME page (a route param, not a
// navigation away). This is that page: it owns every fetch and renders ten
// `ProcessTile`s (`components/ui/ProcessTile.tsx`) plus whichever list the
// route param selects.
//
// COLOUR IS LICENSED, NOT DERIVED (S139 OQ-2 (b)): only four of the ten
// processes have a ruled "by when" — the §21 transfer agreement, the
// leaver's final month, past-deadline, and approved-not-exported. The other
// six get `decisionReady={false}` on every tile below, structurally, so a
// review can check this file's six `false` / four `true` literals rather
// than re-derive readiness from a response — the six have no ruled deadline
// AT ALL, so no response field could carry that fact for them anyway.
//
// TWO READS ARE SUMMARY-FIRST (past-deadline, leaver-final-month): the task
// spec calls these out specifically as "the two enumeration reads" — an
// (employee x month) scan the full item list is comparatively expensive to
// produce just to render a tile count, so the initial paint asks for
// `?summary=true` and the full list is fetched lazily, once, the first time
// that tile is opened (`useLazyFollowUpLoad`). The other seven reads are not
// enumerations; the task spec does not ask for summary mode on them, so they
// are fetched once, in full, on the first paint, and reused as both the tile
// count source and the list content.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ProcessTile } from './ProcessTile'
import { useHrBackdateWorklist, type BackdateWorklistRow } from '../../../hooks/useHrBackdateWorklist'
import { useHrFollowUp, type HrPastDeadlineResponse, type HrLeaverFinalMonthResponse } from '../../../hooks/useHrFollowUp'
import type { ApiResult } from '../../../lib/api'
import WorklistList from './WorklistList'
import {
  PastDeadlineList,
  LeaverFinalMonthList,
  ApprovedNotExportedList,
  UncoveredApproversList,
  CannotRegisterList,
  SettlementReviewsList,
  TerminationPayoutsList,
  TransferAgreementsList,
  PayoutPendingList,
} from './FollowUpLists'
import { daysBetween, daysLabel, formatDaDate, formatDaLongDate } from './followUpFormat'
import styles from './OpfoelgningPage.module.css'

type LoadState<T> = { loading: boolean; error: string | null; data: T | null }
type Loaded<T> = LoadState<T> & { reload: () => void }

const IDLE = <T,>(): LoadState<T> => ({ loading: true, error: null, data: null })

/** Fetches once on mount and exposes `{ loading, error, data, reload }`.
    `fetcher` is expected to be a stable callback (every `useHrFollowUp`
    fetcher is a `useCallback` with an empty dep array). `reload` lets the
    worklist tile refresh its count after `WorklistList`'s one write
    (Resolve) — the only mutation this page's tiles ever need to react to. */
function useFollowUpLoad<T>(fetcher: () => Promise<ApiResult<T>>): Loaded<T> {
  const [state, setState] = useState<LoadState<T>>(IDLE<T>)
  const load = useCallback(() => {
    setState((s) => ({ ...s, loading: true, error: null }))
    void fetcher().then((result) => {
      if (result.ok) setState({ loading: false, error: null, data: result.data })
      else setState({ loading: false, error: result.error, data: null })
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  useEffect(() => {
    load()
  }, [load])
  return { ...state, reload: load }
}

/** Fetches exactly once, the first time `active` becomes true, and caches
    the result thereafter — the lazy "full list" fetch behind past-deadline
    and leaver-final-month's summary-first tiles. */
function useLazyFollowUpLoad<T>(active: boolean, fetcher: () => Promise<ApiResult<T>>): LoadState<T> {
  const [state, setState] = useState<LoadState<T>>({ loading: false, error: null, data: null })
  const startedRef = useRef(false)
  useEffect(() => {
    if (!active || startedRef.current) return
    startedRef.current = true
    let cancelled = false
    setState((s) => ({ ...s, loading: true }))
    void fetcher().then((result) => {
      if (cancelled) return
      if (result.ok) setState({ loading: false, error: null, data: result.data })
      else setState({ loading: false, error: result.error, data: null })
    })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active])
  return state
}

const TILE_KEYS = [
  'worklist',
  'past-deadline',
  'leaver-final-month',
  'approved-not-exported',
  'uncovered-approvers',
  'cannot-register',
  'settlement-reviews',
  'termination-payouts',
  'transfer-agreements',
  'payout-pending',
] as const
type TileKey = (typeof TILE_KEYS)[number]

function isTileKey(value: string | undefined): value is TileKey {
  return value !== undefined && (TILE_KEYS as readonly string[]).includes(value)
}

export function OpfoelgningPage() {
  const params = useParams<{ tile?: string }>()
  const navigate = useNavigate()
  const activeTile: TileKey | null = isTileKey(params.tile) ? params.tile : null

  const openTile = useCallback(
    (key: TileKey) => {
      navigate(activeTile === key ? '/admin/opfoelgning' : `/admin/opfoelgning/${key}`)
    },
    [activeTile, navigate],
  )

  const { fetchWorklist } = useHrBackdateWorklist()
  const {
    fetchPastDeadline,
    fetchLeaverFinalMonth,
    fetchApprovedNotExported,
    fetchUncoveredApprovers,
    fetchCannotRegister,
    fetchSettlementReviews,
    fetchTerminationPayoutsUnrequested,
    fetchTransferAgreementsNeeded,
    fetchPayoutPending,
  } = useHrFollowUp()

  const worklist = useFollowUpLoad<BackdateWorklistRow[]>(fetchWorklist)
  const pastDeadlineSummary = useFollowUpLoad<HrPastDeadlineResponse>(
    useCallback(() => fetchPastDeadline(true), [fetchPastDeadline]),
  )
  const pastDeadlineFull = useLazyFollowUpLoad<HrPastDeadlineResponse>(
    activeTile === 'past-deadline',
    useCallback(() => fetchPastDeadline(false), [fetchPastDeadline]),
  )
  const leaverSummary = useFollowUpLoad<HrLeaverFinalMonthResponse>(
    useCallback(() => fetchLeaverFinalMonth(true), [fetchLeaverFinalMonth]),
  )
  const leaverFull = useLazyFollowUpLoad<HrLeaverFinalMonthResponse>(
    activeTile === 'leaver-final-month',
    useCallback(() => fetchLeaverFinalMonth(false), [fetchLeaverFinalMonth]),
  )
  const approvedNotExported = useFollowUpLoad(fetchApprovedNotExported)
  const uncoveredApprovers = useFollowUpLoad(fetchUncoveredApprovers)
  const cannotRegister = useFollowUpLoad(fetchCannotRegister)
  const settlementReviews = useFollowUpLoad(fetchSettlementReviews)
  const terminationPayouts = useFollowUpLoad(fetchTerminationPayoutsUnrequested)
  const transferAgreements = useFollowUpLoad(fetchTransferAgreementsNeeded)
  const payoutPending = useFollowUpLoad(fetchPayoutPending)

  // ── past-deadline tile numbers (from the summary response) ──────────────
  const pdOldestEmployee = useMemo(() => {
    const d = pastDeadlineSummary.data
    if (!d?.oldestEmployeeLateAnchor) return null
    return daysBetween(d.oldestEmployeeLateAnchor, d.today)
  }, [pastDeadlineSummary.data])
  const pdOldestApprover = useMemo(() => {
    const d = pastDeadlineSummary.data
    if (!d?.oldestApproverLateAnchor) return null
    return daysBetween(d.oldestApproverLateAnchor, d.today)
  }, [pastDeadlineSummary.data])
  const pdOldestOverall = useMemo(() => {
    const candidates = [pdOldestEmployee, pdOldestApprover].filter((v): v is number => v !== null)
    return candidates.length > 0 ? Math.max(...candidates) : null
  }, [pdOldestEmployee, pdOldestApprover])

  // Leaver-final-month and approved-not-exported are "open = all" (S139
  // ruling): unlike past-deadline (whose items are only ever listed once
  // overdue), these two list an item whether or not its deadline has passed
  // yet, so `oldestAnchor` can be in the FUTURE (a leaver whose final month is
  // the current one). `daysBetween` is unclamped by design (past-deadline's
  // tile relies on it never needing a floor), so these two calls clamp to 0
  // here — never "Ældste: -23 dage" — mirroring the list row's own clamp
  // (`FollowUpLists.tsx`'s `daysPastAnchor > 0 ? … : '—'`).
  const leaverOldest = useMemo(() => {
    const d = leaverSummary.data
    if (!d?.oldestAnchor) return null
    return Math.max(0, daysBetween(d.oldestAnchor, d.today))
  }, [leaverSummary.data])

  const notExportedOldest = useMemo(() => {
    const d = approvedNotExported.data
    if (!d?.oldestAnchor) return null
    return Math.max(0, daysBetween(d.oldestAnchor, d.today))
  }, [approvedNotExported.data])

  const cannotRegisterOldest = useMemo(() => {
    const d = cannotRegister.data
    if (!d?.oldestGapSince) return null
    return daysBetween(d.oldestGapSince, d.today)
  }, [cannotRegister.data])

  const uncoveredOldest = useMemo(() => {
    const d = uncoveredApprovers.data
    if (!d?.oldestExpiry) return null
    return daysBetween(d.oldestExpiry, d.today)
  }, [uncoveredApprovers.data])

  const settlementOldest = useMemo(() => {
    const items = settlementReviews.data?.items ?? []
    return items.length > 0 ? Math.max(...items.map((i) => i.ageDays)) : null
  }, [settlementReviews.data])

  const terminationPayoutsOldest = useMemo(() => {
    const items = terminationPayouts.data?.items ?? []
    return items.length > 0 ? Math.max(...items.map((i) => i.ageDays)) : null
  }, [terminationPayouts.data])

  const payoutPendingOldest = useMemo(() => {
    const items = payoutPending.data?.items ?? []
    if (items.length === 0) return null
    const earliest = items.reduce((min, i) => (i.settledAt < min ? i.settledAt : min), items[0].settledAt)
    return formatDaDate(earliest)
  }, [payoutPending.data])

  const worklistCount = worklist.data?.length ?? 0

  return (
    <div className={styles.root}>
      <h1 className={styles.pageTitle}>Opfølgning</h1>
      <p className={styles.pageIntro}>
        Åbne opgaver systemet overlader til HR — ét fliser pr. proces, med den ældste sag øverst i hver liste.
      </p>

      <div className={styles.grid} data-testid="opfoelgning-tile-grid">
        <ProcessTile
          title="Bagudrettede rettelser"
          testId="tile-worklist"
          decisionReady={false}
          onOpen={() => openTile('worklist')}
          count={worklistCount}
          countLabel="åbne sager"
          oldestLabel={null}
        />

        <ProcessTile
          title="Forbi frist"
          testId="tile-past-deadline"
          decisionReady
          onOpen={() => openTile('past-deadline')}
          count={pastDeadlineSummary.data?.employeeLateCount ?? 0}
          countLabel="medarbejder forsinket"
          secondary={{ label: 'Godkender forsinket', value: pastDeadlineSummary.data?.approverLateCount ?? 0 }}
          oldestLabel={pdOldestOverall !== null ? daysLabel(pdOldestOverall) : null}
          footnote={pastDeadlineSummary.data ? `Ser 12 måneder tilbage (fra ${pastDeadlineSummary.data.lookbackFloor}).` : undefined}
        />

        <ProcessTile
          title="Sidste måned ved fratrædelse"
          testId="tile-leaver-final-month"
          decisionReady
          onOpen={() => openTile('leaver-final-month')}
          count={leaverSummary.data?.count ?? 0}
          countLabel="fratrådte"
          oldestLabel={leaverOldest !== null ? daysLabel(leaverOldest) : null}
          footnote={leaverSummary.data ? `Ser 12 måneder tilbage (fra ${leaverSummary.data.lookbackFloor}).` : undefined}
        />

        <ProcessTile
          title="Godkendt, ikke eksporteret"
          testId="tile-approved-not-exported"
          decisionReady
          onOpen={() => openTile('approved-not-exported')}
          count={approvedNotExported.data?.count ?? 0}
          countLabel="måneder"
          oldestLabel={notExportedOldest !== null ? daysLabel(notExportedOldest) : null}
          footnote={approvedNotExported.data ? `Ser 12 måneder tilbage (fra ${approvedNotExported.data.lookbackFloor}).` : undefined}
        />

        <ProcessTile
          title="Uden godkenderdækning"
          testId="tile-uncovered-approvers"
          decisionReady={false}
          onOpen={() => openTile('uncovered-approvers')}
          count={uncoveredApprovers.data?.orphanCount ?? 0}
          countLabel="forældreløse"
          secondary={{ label: 'Udløbne delegeringer', value: uncoveredApprovers.data?.expiredDelegationCount ?? 0 }}
          oldestLabel={uncoveredOldest !== null ? `${daysLabel(uncoveredOldest)} (delegering)` : null}
        />

        <ProcessTile
          title="Kan ikke registrere tid"
          testId="tile-cannot-register"
          decisionReady={false}
          onOpen={() => openTile('cannot-register')}
          count={cannotRegister.data?.count ?? 0}
          countLabel="medarbejdere"
          oldestLabel={cannotRegisterOldest !== null ? daysLabel(cannotRegisterOldest) : null}
        />

        <ProcessTile
          title="Afregninger til gennemgang"
          testId="tile-settlement-reviews"
          decisionReady={false}
          onOpen={() => openTile('settlement-reviews')}
          count={settlementReviews.data?.count ?? 0}
          countLabel="sager"
          oldestLabel={settlementOldest !== null ? daysLabel(settlementOldest) : null}
        />

        <ProcessTile
          title="§26-udbetalinger uden anmodning"
          testId="tile-termination-payouts"
          decisionReady={false}
          onOpen={() => openTile('termination-payouts')}
          count={terminationPayouts.data?.count ?? 0}
          countLabel="fratrædelser"
          oldestLabel={terminationPayoutsOldest !== null ? daysLabel(terminationPayoutsOldest) : null}
        />

        {transferAgreements.data && !transferAgreements.data.windowOpen ? (
          <ProcessTile
            title="§21 — femte ferieuge"
            testId="tile-transfer-agreements"
            decisionReady
            onOpen={() => openTile('transfer-agreements')}
            stateMessage={`Åbner ${formatDaLongDate(transferAgreements.data.windowOpensOn)}`}
          />
        ) : (
          <ProcessTile
            title="§21 — femte ferieuge"
            testId="tile-transfer-agreements"
            decisionReady
            onOpen={() => openTile('transfer-agreements')}
            count={transferAgreements.data?.count ?? 0}
            countLabel="mangler aftale"
            oldestLabel={null}
            footnote={
              transferAgreements.data
                ? `${transferAgreements.data.daysToDeadline} dage til fristen (${formatDaLongDate(transferAgreements.data.deadline)}).`
                : undefined
            }
          />
        )}

        <ProcessTile
          title="Udbetaling afventer afstemning"
          testId="tile-payout-pending"
          decisionReady={false}
          onOpen={() => openTile('payout-pending')}
          count={payoutPending.data?.count ?? 0}
          countLabel="sager"
          oldestLabel={payoutPendingOldest}
        />
      </div>

      <p className={styles.overlapCaveat} data-testid="tile-overlap-caveat">
        En fratrådt medarbejders sidste måned kan tælle med på BÅDE "Forbi frist" og "Sidste måned ved
        fratrædelse" — de to lister hører til forskellige roller, og tallene må ikke lægges sammen.
      </p>

      {activeTile && (
        <div className={styles.listPanel} data-testid="opfoelgning-list-panel">
          {activeTile === 'worklist' && <WorklistList onResolved={worklist.reload} />}
          {activeTile === 'past-deadline' && (
            <PastDeadlineList data={pastDeadlineFull.data} loading={pastDeadlineFull.loading} error={pastDeadlineFull.error} />
          )}
          {activeTile === 'leaver-final-month' && (
            <LeaverFinalMonthList data={leaverFull.data} loading={leaverFull.loading} error={leaverFull.error} />
          )}
          {activeTile === 'approved-not-exported' && (
            <ApprovedNotExportedList data={approvedNotExported.data} loading={approvedNotExported.loading} error={approvedNotExported.error} />
          )}
          {activeTile === 'uncovered-approvers' && (
            <UncoveredApproversList data={uncoveredApprovers.data} loading={uncoveredApprovers.loading} error={uncoveredApprovers.error} />
          )}
          {activeTile === 'cannot-register' && (
            <CannotRegisterList data={cannotRegister.data} loading={cannotRegister.loading} error={cannotRegister.error} />
          )}
          {activeTile === 'settlement-reviews' && (
            <SettlementReviewsList data={settlementReviews.data} loading={settlementReviews.loading} error={settlementReviews.error} />
          )}
          {activeTile === 'termination-payouts' && (
            <TerminationPayoutsList data={terminationPayouts.data} loading={terminationPayouts.loading} error={terminationPayouts.error} />
          )}
          {activeTile === 'transfer-agreements' && (
            <TransferAgreementsList data={transferAgreements.data} loading={transferAgreements.loading} error={transferAgreements.error} />
          )}
          {activeTile === 'payout-pending' && (
            <PayoutPendingList data={payoutPending.data} loading={payoutPending.loading} error={payoutPending.error} />
          )}
        </div>
      )}
    </div>
  )
}
