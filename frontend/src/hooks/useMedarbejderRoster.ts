import { useCallback } from 'react'
import { apiClient, type ApiResult } from '../lib/api'

// S75 TASK-7501. Medarbejder-administration tree page — the FE data layer.
// Read-only roster fetch for the structural ledelseslinjer tree. Mirrors the
// useReportingLines.ts convention: reads go through apiClient, types declared
// locally, the call wrapped in a useCallback'd fetcher returning ApiResult<T>.
//
// The served contract is shipped by 7500 (auth handled server-side):
//   GET /api/admin/reporting-lines/tree/{treeRootOrgId}/medarbejdere
//
// The tree is STRUCTURAL: each person sits under their assigned PRIMARY manager
// (`structuralApproverId`, a raw edge — NOT a resolver projection). `outgoingVikar`
// is a per-away-manager ANNOTATION on the absent manager's own row; reports of an
// away-manager KEEP that away-manager as their `structuralApproverId` (the tree
// does NOT re-root under the vikar). `isRoot` / `isOrphan` are SERVER-computed
// flags — consumers CONSUME them (they are not recomputed from edges).

/** Per-away-manager vikar annotation — present IFF this person is an away-manager
    currently covered by an active vikar. */
export interface OutgoingVikar {
  vikarUserId: string
  vikarDisplayName: string
  untilDate: string // ISO date 'YYYY-MM-DD'
  reason: string
}

/** One employee row in the structural roster (field names are the 7500 contract,
    verbatim). */
export interface MedarbejderRosterRow {
  employeeId: string
  displayName: string
  /** server already applied `?? primaryOrgName` — ALWAYS a string, never null. */
  enhedLabel: string
  position: string | null
  /** the person's assigned active PRIMARY manager — THE TREE KEY (raw edge). */
  structuralApproverId: string | null
  periodStatus: 'OPEN' | 'SUBMITTED' | 'APPROVED'
  outgoingVikar: OutgoingVikar | null
  /** server-computed (R3): no active PRIMARY approver AND is the
      structuralApproverId of >=1 person. */
  isRoot: boolean
  /** server-computed (R3): no active PRIMARY approver AND approves no one. */
  isOrphan: boolean
}

/** The full roster response: the flat employee array + the pending-count tile feed. */
export interface MedarbejderRosterResponse {
  employees: MedarbejderRosterRow[]
  /** managerId -> count of pending (SUBMITTED) periods routed to them. */
  pendingCountByManager: Record<string, number>
}

export function useMedarbejderRoster() {
  const fetchRoster = useCallback(
    async (treeRootOrgId: string): Promise<ApiResult<MedarbejderRosterResponse>> => {
      return apiClient.get<MedarbejderRosterResponse>(
        `/api/admin/reporting-lines/tree/${encodeURIComponent(treeRootOrgId)}/medarbejdere`,
      )
    },
    [],
  )

  return { fetchRoster }
}
