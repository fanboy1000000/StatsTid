// S75 TASK-7501. Pure structural-tree derivation helpers for the
// Medarbejder-administration page (7502 consumes these). Ported from the
// prototype graph/tree helpers
// (design_handoff_medarbejder_administration/.../ledelseslinjer-{data,tree}.jsx)
// and re-typed over the served contract row.
//
// They operate on `structuralApproverId` (the raw PRIMARY edge — NOT a resolver).
// The served `isRoot` / `isOrphan` flags are CONSUMED, never recomputed from edges.
//
// R4 CYCLE-SAFETY (load-bearing): the prototype `depthMap.walk` had NO visited
// guard and would infinite-loop on a structural cycle (legacy data + the
// un-cycle-guarded /delegate vikar path can produce one). Every walk here carries
// a visited-set guard so it terminates on a cycle.

import type { MedarbejderRosterRow } from '../../hooks/useMedarbejderRoster'

/** Map employeeId -> row. */
export function indexBy(
  people: MedarbejderRosterRow[],
): Record<string, MedarbejderRosterRow> {
  return Object.fromEntries(people.map((p) => [p.employeeId, p]))
}

/** Direct reports of `id` — people whose structural PRIMARY manager is `id`. */
export function childrenOf(
  people: MedarbejderRosterRow[],
  id: string,
): MedarbejderRosterRow[] {
  return people.filter((p) => p.structuralApproverId === id)
}

/** Roots — the server-computed `isRoot` flag (top of a line). */
export function rootsOf(people: MedarbejderRosterRow[]): MedarbejderRosterRow[] {
  return people.filter((p) => p.isRoot)
}

/** Orphans — the server-computed `isOrphan` flag (broken line, no approver, no reports). */
export function orphansOf(people: MedarbejderRosterRow[]): MedarbejderRosterRow[] {
  return people.filter((p) => p.isOrphan)
}

/** True if `id` is the structural manager of at least one person. */
export function isManager(people: MedarbejderRosterRow[], id: string): boolean {
  return people.some((p) => p.structuralApproverId === id)
}

/** Transitive descendants of `id` (employeeIds). Cycle-safe via the `out` set
    guard — preserved verbatim from the prototype. */
export function descendantsOf(
  people: MedarbejderRosterRow[],
  id: string,
): Set<string> {
  const out = new Set<string>()
  const walk = (pid: string): void => {
    childrenOf(people, pid).forEach((c) => {
      if (!out.has(c.employeeId)) {
        out.add(c.employeeId)
        walk(c.employeeId)
      }
    })
  }
  walk(id)
  return out
}

/** 0-based depth of every person, walking from the roots.
    R4: a `visited` set guards against structural cycles so the walk terminates. */
export function depthMap(people: MedarbejderRosterRow[]): Record<string, number> {
  const d: Record<string, number> = {}
  const visited = new Set<string>()
  const walk = (p: MedarbejderRosterRow, depth: number): void => {
    if (visited.has(p.employeeId)) return
    visited.add(p.employeeId)
    d[p.employeeId] = depth
    childrenOf(people, p.employeeId).forEach((c) => walk(c, depth + 1))
  }
  rootsOf(people).forEach((r) => walk(r, 0))
  return d
}

/** Collapsed-set that shows exactly levels 1..L (1-based). `Infinity` (show all)
    returns the empty set. Cycle-safe via the guarded `depthMap`. */
export function collapsedForLevel(
  people: MedarbejderRosterRow[],
  L: number,
): Set<string> {
  if (!Number.isFinite(L)) return new Set()
  const d = depthMap(people)
  const set = new Set<string>()
  people.forEach((p) => {
    if (childrenOf(people, p.employeeId).length > 0 && (d[p.employeeId] ?? 0) >= L - 1) {
      set.add(p.employeeId)
    }
  })
  return set
}

/** Default collapse: "team leads" — managers whose reports are all individuals —
    so the front line isn't dumped at once. Higher managers stay open. */
export function defaultCollapsed(people: MedarbejderRosterRow[]): Set<string> {
  const set = new Set<string>()
  people.forEach((p) => {
    const kids = childrenOf(people, p.employeeId)
    if (kids.length > 0 && kids.every((k) => !isManager(people, k.employeeId))) {
      set.add(p.employeeId)
    }
  })
  return set
}

/** Case-insensitive substring match over displayName + position + enhedLabel.
    `q` is expected already-lowercased by the caller (matching the prototype). */
export function queryHit(p: MedarbejderRosterRow, q: string): boolean {
  return [p.displayName, p.position, p.enhedLabel].some(
    (f) => !!f && f.toLowerCase().includes(q),
  )
}

/** One ordered, depth-annotated row in the rendered tree. */
export interface VisibleTreeRow {
  row: MedarbejderRosterRow
  depth: number
}

/**
 * The ordered render-walk: a pre-order traversal from the roots producing
 * `{ row, depth }[]`. Cycle-safe via a `visited` guard (R4).
 *
 * - `collapsed`: a person in this set has its subtree hidden (not traversed),
 *   UNLESS a `visibleSet` is active (search/filter mode forces the matching
 *   subtrees open).
 * - `visibleSet` (optional): when present, only people in the set are emitted
 *   AND children are always traversed (collapse is ignored) — this mirrors the
 *   prototype's search-context behaviour where the matched nodes + their
 *   ancestor chain stay visible regardless of collapse state.
 */
export function visibleTreeRows(
  people: MedarbejderRosterRow[],
  collapsed: Set<string>,
  visibleSet?: Set<string> | null,
): VisibleTreeRow[] {
  const rows: VisibleTreeRow[] = []
  const visited = new Set<string>()
  const walk = (person: MedarbejderRosterRow, depth: number): void => {
    if (visited.has(person.employeeId)) return
    if (visibleSet && !visibleSet.has(person.employeeId)) return
    visited.add(person.employeeId)
    rows.push({ row: person, depth })
    if (visibleSet || !collapsed.has(person.employeeId)) {
      childrenOf(people, person.employeeId).forEach((c) => walk(c, depth + 1))
    }
  }
  rootsOf(people).forEach((r) => walk(r, 0))
  return rows
}
