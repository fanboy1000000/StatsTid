// SPRINT-140 / TASK-14006 (refinement B4) — the nine HR follow-up reads the
// landing page (`/admin/opfoelgning`) renders behind ProcessTile, plus the
// pre-existing payout-pending read (HRP-006). All are HROrAbove, org-scoped
// server-side (`GetAccessibleOrgsAsync(actor, LocalHR)`, empty scope -> 403 —
// this hook does not paper over that; it surfaces the fetch error like every
// other read here). The backdate worklist (the tenth tile) rides its own
// existing `useHrBackdateWorklist` (S140/TASK-14007) — not duplicated here.
//
// `?summary=true` is used ONLY for the two (employee x month) ENUMERATION
// reads (past-deadline, leaver-final-month) on the landing page's first
// paint, per the task spec: those two are the reads whose full item list is
// expensive to compute purely to render a tile count. The other seven reads
// are fetched in full on first paint — none of them is an enumeration, and
// the task spec does not ask for summary mode on them.
//
// Every call rides the GENERATED spec-keyed typed client (PAT-012) — no
// hand-written wire shapes.
import { useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

type Schemas = components['schemas']

export type HrFollowUpMonthItem = Schemas['StatsTid.Backend.Api.Contracts.HrFollowUpMonthItem']
export type HrPastDeadlineResponse = Schemas['StatsTid.Backend.Api.Contracts.HrPastDeadlineResponse']
export type HrLeaverFinalMonthResponse = Schemas['StatsTid.Backend.Api.Contracts.HrLeaverFinalMonthResponse']
export type HrApprovedNotExportedResponse = Schemas['StatsTid.Backend.Api.Contracts.HrApprovedNotExportedResponse']
export type HrUncoveredApproversResponse = Schemas['StatsTid.Backend.Api.Contracts.HrUncoveredApproversResponse']
export type HrOrphanEmployee = Schemas['StatsTid.Backend.Api.Contracts.HrOrphanEmployee']
export type HrExpiredDelegation = Schemas['StatsTid.Backend.Api.Contracts.HrExpiredDelegation']
export type HrCannotRegisterResponse = Schemas['StatsTid.Backend.Api.Contracts.HrCannotRegisterResponse']
export type HrCannotRegisterEmployee = Schemas['StatsTid.Backend.Api.Contracts.HrCannotRegisterEmployee']
export type PendingSettlementReviewListResponse =
  Schemas['StatsTid.Backend.Api.Contracts.PendingSettlementReviewListResponse']
export type PendingSettlementReviewItem = Schemas['StatsTid.Backend.Api.Contracts.PendingSettlementReviewItem']
export type TerminationPayoutUnrequestedListResponse =
  Schemas['StatsTid.Backend.Api.Contracts.TerminationPayoutUnrequestedListResponse']
export type TerminationPayoutUnrequestedItem =
  Schemas['StatsTid.Backend.Api.Contracts.TerminationPayoutUnrequestedItem']
export type VacationTransferAgreementNeededListResponse =
  Schemas['StatsTid.Backend.Api.Contracts.VacationTransferAgreementNeededListResponse']
export type VacationTransferAgreementNeededItem =
  Schemas['StatsTid.Backend.Api.Contracts.VacationTransferAgreementNeededItem']
export type VacationTransferAgreementCannotComputeItem =
  Schemas['StatsTid.Backend.Api.Contracts.VacationTransferAgreementCannotComputeItem']
export type PayoutPendingListResponse = Schemas['StatsTid.Backend.Api.Contracts.PayoutPendingListResponse']
export type PayoutPendingItem = Schemas['StatsTid.Backend.Api.Contracts.PayoutPendingItem']

export function useHrFollowUp() {
  const fetchPastDeadline = useCallback((summary: boolean) => {
    return apiClient.get('/api/hr/follow-up/past-deadline', { query: { summary } })
  }, [])

  const fetchLeaverFinalMonth = useCallback((summary: boolean) => {
    return apiClient.get('/api/hr/follow-up/leaver-final-month', { query: { summary } })
  }, [])

  const fetchApprovedNotExported = useCallback(() => {
    return apiClient.get('/api/hr/follow-up/approved-not-exported')
  }, [])

  const fetchUncoveredApprovers = useCallback(() => {
    return apiClient.get('/api/hr/follow-up/uncovered-approvers')
  }, [])

  const fetchCannotRegister = useCallback(() => {
    return apiClient.get('/api/hr/follow-up/cannot-register')
  }, [])

  const fetchSettlementReviews = useCallback(() => {
    return apiClient.get('/api/hr/follow-up/settlement-reviews')
  }, [])

  const fetchTerminationPayoutsUnrequested = useCallback(() => {
    return apiClient.get('/api/hr/follow-up/termination-payouts-unrequested')
  }, [])

  const fetchTransferAgreementsNeeded = useCallback(() => {
    return apiClient.get('/api/hr/follow-up/transfer-agreements-needed')
  }, [])

  const fetchPayoutPending = useCallback(() => {
    return apiClient.get('/api/vacation-settlements/payout-pending')
  }, [])

  return {
    fetchPastDeadline,
    fetchLeaverFinalMonth,
    fetchApprovedNotExported,
    fetchUncoveredApprovers,
    fetchCannotRegister,
    fetchSettlementReviews,
    fetchTerminationPayoutsUnrequested,
    fetchTransferAgreementsNeeded,
    fetchPayoutPending,
  }
}
