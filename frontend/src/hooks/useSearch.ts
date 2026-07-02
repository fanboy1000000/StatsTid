// SPRINT-107 / TASK-10704 (Enhedsspor Phase 3b-1) — the data layer for the merged
// "Organisation & medarbejdere" admin page's SEARCH overlay.
//
// Consumes the S106 unified scoped units+people SEARCH read:
//   GET /api/admin/search?q=... → { units: UnitSearchResult[], people: PersonSearchResult[] }
//
// The search is SERVER-side (the FE lazy-loads the roster per Organisation, so a
// pure client filter could never see un-loaded people). It is ALREADY
// scope-bounded server-side (ADR-038 D5 / P7): a scoped HR gets NO
// cross-Organisation results — units AND people are admitted SOLELY by the
// actor's accessible-org set. The Afgrænsning is a pure VIEW narrowing the FE
// applies ON TOP of that admitted set (never a widening).
//
// S113 / TASK-11301 — the response types are the GENERATED spec types VERBATIM
// (`api-types.ts`, strict since the S113 `required`-emission): the S111 coercion
// + drift-guard scaffolding (the deleted apiNarrow module) is gone; a renamed or
// removed backend field is now a direct `tsc` error at the `setResults` call
// (the S97→S99→S100 "fetchEnheder" drift class, closed structurally). Note
// `organisationId` on BOTH result kinds — the key the S107 Afgrænsning filters by
// (an id, never the fragile path text).
//
// LINT (PAT-010): the URL is passed INLINE as a literal template to
// apiClient.get so the contract-coverage lint (tools/check_endpoint_contracts.py)
// can enumerate it — a path-helper const would evade the gate (the documented
// blind spot). The static prefix `/api/admin/search` is what the lint normalizes
// (the `?q=${...}` query is stripped). TASK-10705 registers it in the lint.

import { useState, useEffect } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

type Schemas = components['schemas']

/** A matching ACTIVE unit (ENHEDER section). `path` is the breadcrumb the overlay
    shows — the chain of names from the Organisation (root) down to the unit's
    immediate parent, EXCLUSIVE of the unit's own `name` (a top-level unit's path
    is just `[OrganisationName]`). `organisationId` is the unit's immutable
    Organisation (the Afgrænsning scope-filter key). */
export type UnitSearchResult = Schemas['StatsTid.Backend.Api.Contracts.UnitSearchResult']

/** A matching ACTIVE person (MEDARBEJDERE section). `organisationId` is the
    person's immutable primary Organisation — the SAME id the search scope admits
    by, and the key the S107 Afgrænsning filters the people by (NOT the fragile
    `path` text). `path` is the breadcrumb from the Organisation (root) down to and
    INCLUDING the home unit. `unitName` is null for an Organisation-homed person. */
export type PersonSearchResult = Schemas['StatsTid.Backend.Api.Contracts.PersonSearchResult']

/** The GET /api/admin/search envelope — `{ units, people, unitsTotal, peopleTotal }`
    (the design's TWO-section overlay shape; NOT a bare array — the S97/S99 envelope
    distinction).

    S110 / TASK-11002 — `unitsTotal` / `peopleTotal` are the EXACT per-section match
    counts BEFORE the server's page cap (200/section). They are >= the returned
    `units.length` / `people.length`; a section whose returned list is shorter than its
    total was TRUNCATED by the cap. The overlay surfaces this as an honest "N flere"
    signal so a capped section is not mistaken for complete (the count compares the
    SERVER total against the SERVER-returned count — independent of the client-side
    Afgrænsning narrowing, which can only ever shrink the displayed set further). */
export type SearchResponse = Schemas['StatsTid.Backend.Api.Contracts.SearchResponse']

const EMPTY: SearchResponse = { units: [], people: [], unitsTotal: 0, peopleTotal: 0 }
const DEBOUNCE_MS = 250

/**
 * The debounced scoped search (GET /api/admin/search) for the overlay. `setQuery`
 * is wired to the overlay input; the fetch fires DEBOUNCE_MS after the last
 * keystroke (a stale in-flight request is superseded by clearing its timer + a
 * `cancelled` guard, so a late response never clobbers a newer query). An
 * empty/blank query resets to the idle empty result without a request.
 */
export function useSearch() {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResponse>(EMPTY)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const q = query.trim()
    if (!q) {
      setResults(EMPTY)
      setLoading(false)
      setError(null)
      return
    }

    let cancelled = false
    setLoading(true)
    setError(null)
    const handle = setTimeout(async () => {
      // S111 / TASK-11102 — typed via the OpenAPI path key with the structured
      // `query` shape ({ q }); `apiClient` appends `?q=…` (URL-encoded).
      // `result.data` IS the strict spec `SearchResponse` (no coercion — S113).
      const result = await apiClient.get('/api/admin/search', { query: { q } })
      if (cancelled) return
      if (result.ok) {
        setResults(result.data)
      } else {
        setError(result.error)
        setResults(EMPTY)
      }
      setLoading(false)
    }, DEBOUNCE_MS)

    return () => {
      cancelled = true
      clearTimeout(handle)
    }
  }, [query])

  return { query, setQuery, results, loading, error }
}
