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

// R3 (S77) PERFORMANCE: the derivations below (depthMap / visibleTreeRows /
// defaultCollapsed / collapsedForLevel / descendantsOf) all walk the tree and ask
// "who are the children of X?" once per node. Done naively via a full-array
// `filter` per node, that is O(n) per node → O(n²) over the whole tree (a 2000-
// node styrelse = up to ~4M comparisons per derivation; a degenerate deep chain
// is the worst case). The fix is a single precomputed ADJACENCY INDEX
// (parentId -> child[]) built ONCE in O(n); every subsequent child lookup is then
// O(1) and the whole pipeline is O(n). The build preserves the original
// `people.filter` ORDER (a stable single pass), so the derivations emit identical
// rows / depths / collapse sets — this is a behaviour-preserving optimisation.
//
// The S75 R4 cycle-safety guards (visited-set / `out`-set) are PRESERVED VERBATIM
// in every walk below — they are now load-bearing once S76 write flows can
// introduce a structural cycle. Only the child-lookup mechanism changed.

/** Adjacency index: structuralApproverId -> the rows that report to it, in the
    SAME order `people.filter` would have produced (insertion order of `people`).
    Built in one O(n) pass. `null` approvers are simply not indexed (roots/orphans
    are reached via `rootsOf`, never as someone's child). */
type ChildIndex = Map<string, MedarbejderRosterRow[]>

function buildChildIndex(people: MedarbejderRosterRow[]): ChildIndex {
  const idx: ChildIndex = new Map()
  for (const p of people) {
    const parent = p.structuralApproverId
    if (parent == null) continue
    const bucket = idx.get(parent)
    if (bucket) bucket.push(p)
    else idx.set(parent, [p])
  }
  return idx
}

/** O(1) child lookup against a prebuilt index (empty array when none). */
function childrenFromIndex(idx: ChildIndex, id: string): MedarbejderRosterRow[] {
  return idx.get(id) ?? []
}

/** Map employeeId -> row. */
export function indexBy(
  people: MedarbejderRosterRow[],
): Record<string, MedarbejderRosterRow> {
  return Object.fromEntries(people.map((p) => [p.employeeId, p]))
}

/** Direct reports of `id` — people whose structural PRIMARY manager is `id`.
    Public single-lookup convenience (callers use it for a one-off count); the
    internal multi-node derivations use a prebuilt index instead (R3). */
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
    guard — same semantics as the prototype's recursive walk (a node is only
    visited once; the `out`-set membership cuts cycles), but ITERATIVE (explicit
    stack) so a degenerate deep chain cannot overflow the JS call stack. O(n) via
    the prebuilt index. */
export function descendantsOf(
  people: MedarbejderRosterRow[],
  id: string,
): Set<string> {
  const idx = buildChildIndex(people)
  const out = new Set<string>()
  // Seed with `id`'s direct children, in order. Reverse-push so popping yields
  // the original child order (matches the prototype's forEach recursion order).
  const stack: string[] = childrenFromIndex(idx, id)
    .map((c) => c.employeeId)
    .reverse()
  while (stack.length > 0) {
    const cur = stack.pop()!
    if (out.has(cur)) continue // already seen → cycle cut (the `out`-set guard)
    out.add(cur)
    const kids = childrenFromIndex(idx, cur)
    for (let i = kids.length - 1; i >= 0; i--) stack.push(kids[i].employeeId)
  }
  return out
}

/** 0-based depth of every person, walking from the roots.
    R4: a `visited` set guards against structural cycles so the walk terminates.
    ITERATIVE (explicit stack) so a deep chain cannot overflow the call stack;
    the visited-before-assign semantics and the root pre-order are preserved. */
export function depthMap(people: MedarbejderRosterRow[]): Record<string, number> {
  const idx = buildChildIndex(people)
  const d: Record<string, number> = {}
  const visited = new Set<string>()
  // A LIFO stack of {row, depth}. Roots are pushed in reverse so the first root
  // is processed first; each node's children are pushed in reverse so they pop in
  // original order — i.e. an identical pre-order to the recursive walk.
  const stack: { row: MedarbejderRosterRow; depth: number }[] = []
  const roots = rootsOf(people)
  for (let i = roots.length - 1; i >= 0; i--) stack.push({ row: roots[i], depth: 0 })
  while (stack.length > 0) {
    const { row, depth } = stack.pop()!
    if (visited.has(row.employeeId)) continue // cycle / re-entry guard (R4)
    visited.add(row.employeeId)
    d[row.employeeId] = depth
    const kids = childrenFromIndex(idx, row.employeeId)
    for (let i = kids.length - 1; i >= 0; i--) stack.push({ row: kids[i], depth: depth + 1 })
  }
  return d
}

/** Collapsed-set that shows exactly levels 1..L (1-based). `Infinity` (show all)
    returns the empty set. Cycle-safe via the guarded `depthMap`. */
export function collapsedForLevel(
  people: MedarbejderRosterRow[],
  L: number,
): Set<string> {
  if (!Number.isFinite(L)) return new Set()
  const idx = buildChildIndex(people)
  const d = depthMap(people)
  const set = new Set<string>()
  people.forEach((p) => {
    if (childrenFromIndex(idx, p.employeeId).length > 0 && (d[p.employeeId] ?? 0) >= L - 1) {
      set.add(p.employeeId)
    }
  })
  return set
}

/** Default collapse: "team leads" — managers whose reports are all individuals —
    so the front line isn't dumped at once. Higher managers stay open. */
export function defaultCollapsed(people: MedarbejderRosterRow[]): Set<string> {
  const idx = buildChildIndex(people)
  // A person is a manager iff they appear as a parent in the index — an O(1)
  // check that replaces the per-node `isManager` full-array scan (R3).
  const set = new Set<string>()
  people.forEach((p) => {
    const kids = childrenFromIndex(idx, p.employeeId)
    if (kids.length > 0 && kids.every((k) => !idx.has(k.employeeId))) {
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
  const idx = buildChildIndex(people)
  const rows: VisibleTreeRow[] = []
  const visited = new Set<string>()
  // ITERATIVE pre-order (explicit stack) so a deep chain cannot overflow the call
  // stack. The per-node decisions are byte-identical to the recursive walk:
  //   1. already visited → skip (cycle/re-entry guard, R4)
  //   2. visibleSet active & node not in it → skip entirely (no emit, no children)
  //   3. else emit, and descend into children UNLESS collapsed (and no visibleSet)
  // Children + roots are reverse-pushed so they pop in original order.
  const stack: { row: MedarbejderRosterRow; depth: number }[] = []
  const roots = rootsOf(people)
  for (let i = roots.length - 1; i >= 0; i--) stack.push({ row: roots[i], depth: 0 })
  while (stack.length > 0) {
    const { row: person, depth } = stack.pop()!
    if (visited.has(person.employeeId)) continue
    if (visibleSet && !visibleSet.has(person.employeeId)) continue
    visited.add(person.employeeId)
    rows.push({ row: person, depth })
    if (visibleSet || !collapsed.has(person.employeeId)) {
      const kids = childrenFromIndex(idx, person.employeeId)
      for (let i = kids.length - 1; i >= 0; i--) stack.push({ row: kids[i], depth: depth + 1 })
    }
  }
  return rows
}
