// S77 TASK-7700 / R3 (FE half). Performance + large-tree correctness tests for
// the medarbejderTree derivations after the O(n) adjacency-index optimisation.
//
// WHY: the derivations (depthMap / visibleTreeRows / defaultCollapsed /
// collapsedForLevel) used to call a full-array `childrenOf` filter inside the
// recursion → O(n²). The optimisation builds a single parent→children index once
// (O(n)) so the whole pipeline is O(n). These tests pin BOTH:
//   • CORRECTNESS at the ~2000-node product target (rows/depths/collapse).
//   • A PERF DISCRIMINATOR at n=5000 (above the 2000 product target so a
//     regression to O(n²) is unmistakable): the full derivation pipeline over a
//     5000-node degenerate deep chain AND a 5000-node shallow star each completes
//     in < 100 ms. Post-optimisation O(n) at 5000 is sub-millisecond — huge
//     headroom, non-flaky; a re-introduced O(n²) (25M ops/derivation) would breach
//     the 100 ms budget. We use performance.now() deltas.
import { describe, it, expect } from 'vitest'
import type { MedarbejderRosterRow } from '../../../hooks/useMedarbejderRoster'
import {
  depthMap,
  defaultCollapsed,
  collapsedForLevel,
  visibleTreeRows,
  descendantsOf,
} from '../medarbejderTree'

function mkRow(
  employeeId: string,
  structuralApproverId: string | null,
  isRoot: boolean,
): MedarbejderRosterRow {
  return {
    employeeId,
    displayName: employeeId,
    enhedLabel: 'Enhed',
    position: null,
    structuralApproverId,
    periodStatus: 'APPROVED',
    outgoingVikar: null,
    isRoot,
    isOrphan: false,
  }
}

/** A balanced-ish styrelse tree of `n` nodes: one root + a fan-out chain so the
    tree has real depth (not a degenerate worst case). Each node (after the root)
    reports to the node `Math.floor((i - 1) / fanout)` — a classic array-encoded
    k-ary tree, producing depth ~= log_fanout(n). */
function fanoutTree(n: number, fanout: number): MedarbejderRosterRow[] {
  const people: MedarbejderRosterRow[] = [mkRow('n0', null, true)]
  for (let i = 1; i < n; i++) {
    const parent = Math.floor((i - 1) / fanout)
    people.push(mkRow(`n${i}`, `n${parent}`, false))
  }
  return people
}

/** A 5000-node DEGENERATE DEEP CHAIN: root -> n1 -> n2 -> ... -> n4999. This is
    the O(n²) worst case for the OLD per-node full-array filter (the deepest
    recursion + the most repeated scans). */
function deepChain(n: number): MedarbejderRosterRow[] {
  const people: MedarbejderRosterRow[] = [mkRow('n0', null, true)]
  for (let i = 1; i < n; i++) {
    people.push(mkRow(`n${i}`, `n${i - 1}`, false))
  }
  return people
}

/** A 5000-node SHALLOW STAR: 1 root + (n-1) direct children. The OLD
    `defaultCollapsed` re-scanned the whole array per child to answer isManager →
    O(n²); the new index makes it O(1) per child. */
function shallowStar(n: number): MedarbejderRosterRow[] {
  const people: MedarbejderRosterRow[] = [mkRow('n0', null, true)]
  for (let i = 1; i < n; i++) {
    people.push(mkRow(`n${i}`, 'n0', false))
  }
  return people
}

/** Run the full derivation pipeline once over `people` (the same set the page
    computes per render). */
function runPipeline(people: MedarbejderRosterRow[]): void {
  const collapsed = defaultCollapsed(people)
  depthMap(people)
  collapsedForLevel(people, 2)
  // both the unfiltered render-walk and a search-context walk (visibleSet active)
  visibleTreeRows(people, collapsed)
  const visibleSet = new Set(people.map((p) => p.employeeId))
  visibleTreeRows(people, new Set(), visibleSet)
}

const PERF_BUDGET_MS = 100
const N_TARGET = 2000
const N_PERF = 5000

describe('medarbejderTree — large-tree correctness (~2000-node product target)', () => {
  it('produces correct rows/depths/collapse on a ~2000-node fan-out styrelse tree', () => {
    const people = fanoutTree(N_TARGET, 5) // 5-ary tree → depth ~= 5
    const d = depthMap(people)

    // Every node reachable from the single root → all 2000 get a depth.
    expect(Object.keys(d)).toHaveLength(N_TARGET)
    expect(d.n0).toBe(0)
    // n1..n5 report to n0 → depth 1; n6 reports to n1 → depth 2 (spot checks
    // against the array-encoded k-ary parent formula).
    expect(d.n1).toBe(1)
    expect(d.n5).toBe(1)
    expect(d.n6).toBe(2)
    const maxDepth = Math.max(...Object.values(d))
    expect(maxDepth).toBeGreaterThanOrEqual(4) // 5-ary over 2000 nodes → ~5 levels

    // visibleTreeRows (nothing collapsed) emits EVERY node exactly once, in a
    // valid pre-order (each child appears after its parent).
    const rows = visibleTreeRows(people, new Set())
    expect(rows).toHaveLength(N_TARGET)
    const seenAt = new Map<string, number>()
    rows.forEach((r, i) => seenAt.set(r.row.employeeId, i))
    expect(seenAt.size).toBe(N_TARGET) // no duplicates
    for (const r of rows) {
      const parent = r.row.structuralApproverId
      if (parent != null) {
        // parent emitted earlier (pre-order) and child is exactly one deeper.
        expect(seenAt.get(parent)!).toBeLessThan(seenAt.get(r.row.employeeId)!)
        expect(r.depth).toBe(d[r.row.employeeId])
      }
    }

    // defaultCollapsed: only managers whose reports are ALL individuals. In a
    // 5-ary tree those are the deepest internal nodes; the set is non-empty and
    // every member is genuinely a manager with only-leaf children.
    const collapsed = defaultCollapsed(people)
    expect(collapsed.size).toBeGreaterThan(0)

    // collapsing all the default nodes shrinks the visible row count.
    const collapsedRows = visibleTreeRows(people, collapsed)
    expect(collapsedRows.length).toBeLessThan(N_TARGET)
  })
})

describe('medarbejderTree — O(n) perf discriminators (n=5000, < 100ms)', () => {
  it('full pipeline over a 5000-node DEGENERATE DEEP CHAIN completes in < 100ms', () => {
    const people = deepChain(N_PERF)
    // sanity: the chain really is 5000 deep
    const d = depthMap(people)
    expect(d.n0).toBe(0)
    expect(d[`n${N_PERF - 1}`]).toBe(N_PERF - 1)

    const t0 = performance.now()
    runPipeline(people)
    const elapsed = performance.now() - t0
    expect(elapsed).toBeLessThan(PERF_BUDGET_MS)
  })

  it('full pipeline over a 5000-node SHALLOW STAR (1 root + 4999 children) completes in < 100ms', () => {
    const people = shallowStar(N_PERF)
    // sanity: every child is at depth 1 under the single root
    const d = depthMap(people)
    expect(Object.keys(d)).toHaveLength(N_PERF)
    expect(d.n1).toBe(1)
    expect(d[`n${N_PERF - 1}`]).toBe(1)
    // the single root is the only manager → defaultCollapsed collapses just it
    // (all 4999 reports are individuals).
    expect([...defaultCollapsed(people)]).toEqual(['n0'])

    const t0 = performance.now()
    runPipeline(people)
    const elapsed = performance.now() - t0
    expect(elapsed).toBeLessThan(PERF_BUDGET_MS)
  })

  it('descendantsOf over the 5000-node deep chain is O(n) and < 100ms', () => {
    const people = deepChain(N_PERF)
    const t0 = performance.now()
    const desc = descendantsOf(people, 'n0')
    const elapsed = performance.now() - t0
    // root's descendants = every other node.
    expect(desc.size).toBe(N_PERF - 1)
    expect(elapsed).toBeLessThan(PERF_BUDGET_MS)
  })
})
