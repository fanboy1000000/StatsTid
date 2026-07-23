import { useState, useEffect, useCallback, useRef } from 'react'
import type { components } from '../lib/api-types'
import { apiClient } from '../lib/api'

// S65 / TASK-6503 — read-only year-overview contract consumed by ArsoversigtPage
// (Direction E Årsoversigt): every quantity is server-computed; the FE never
// recomputes a past/current/future classification — it derives those from
// `today` ONLY.
//
// S120 / TASK-12001 (Typed API Contract retrofit Pass 7, PAT-012) — the read
// rides the TYPED spec-keyed form; the hand-written `YearOverview` + 5 sub
// interfaces were DELETED in favour of the aliases below. Lie-audit deltas:
//  - each category's REQUIRED (nullable-complex) `settlement` member was
//    OMITTED by the FE (owner ruling #2 normalized the empty-config branch to
//    always emit it, null-valued) — it surfaces additively, display-only;
//  - `tiles.ferieRemaining`/`tiles.careDayRemaining` are NULLABLE on the wire
//    — the hand-written types claimed non-null (the page's TileSpec already
//    handled null, so the lie was latent).
//
// RESOLVED (same sprint): the discrepancy this file originally DECLARED —
// the spec emitting `saldo: number[]` while the wire serves null cells (the
// empty-config branch's ALL-null 12-array, `new decimal?[12]`,
// BalanceEndpoints.cs:898; C# member `decimal?[]`) — was fixed at the SOURCE:
// the S120 ruling-#2 runtime pin REDded on it, and the spec emission now marks
// nullable-value-type array items `nullable: true` (ResponseStrictTypesFilter,
// the nullable-ITEMS sibling of the S117 nullable-`$ref` class, instance #1
// fixed at first firing). The generated type is natively `(number | null)[]`,
// so the interim FE-side `Omit`-widening was removed — the aliases below are
// the plain spec types.

type SpecCategory = components['schemas']['StatsTid.Backend.Api.Contracts.YearOverviewCategory']
type SpecResponse = components['schemas']['StatsTid.Backend.Api.Contracts.YearOverviewResponse']

/** Header context line: identity + dated agreement/OK + weekly norm. */
export type YearOverviewHeader =
  components['schemas']['StatsTid.Backend.Api.Contracts.YearOverviewHeader']

/** The designed 6 balance tiles (Særlige feriedage is matrix-only — no 7th tile). */
export type YearOverviewTiles =
  components['schemas']['StatsTid.Backend.Api.Contracts.YearOverviewTiles']

/** One calendar month (index 0..11 = Jan..Dec of the selected year). */
export type YearOverviewMonth =
  components['schemas']['StatsTid.Backend.Api.Contracts.YearOverviewMonth']

/** The S117 settlement disposition record (spec-shared with /summary rows). */
export type SettlementDispositionInfo =
  components['schemas']['StatsTid.Backend.Api.Contracts.SettlementDispositionInfo']

/** One absence category group — `saldo` is `(number | null)[]` straight from
    the spec: a cell is null when the server cannot compute a saldo (the
    empty-config branch); the UI renders null as an em-dash. */
export type YearOverviewCategory = SpecCategory

export type YearOverview = SpecResponse

export function useYearOverview(employeeId: string, year: number) {
  const [data, setData] = useState<YearOverview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Stale-response guard: rapid ←/→ year switches fire overlapping requests that
  // can resolve out of order. Each invocation claims a monotonically increasing
  // id; only the latest in-flight request is allowed to commit state, so an older
  // year's response can never overwrite a newer one.
  const latestRequestId = useRef(0)

  const fetchOverview = useCallback(async () => {
    if (!employeeId) return
    const requestId = ++latestRequestId.current
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/balance/{employeeId}/year-overview', {
      params: { path: { employeeId } },
      query: { year },
    })
    // A newer request superseded this one while it was in flight — drop the result.
    if (requestId !== latestRequestId.current) return
    if (result.ok) {
      setData(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId, year])

  useEffect(() => {
    fetchOverview()
  }, [fetchOverview])

  return { data, loading, error, refetch: fetchOverview }
}
