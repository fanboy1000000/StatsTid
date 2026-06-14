// S75 TASK-7501. Unit tests for the pure structural-tree derivation helpers.
// Focus is the helpers (the hook itself is test-light, mirroring the repo
// convention where data-fetching hooks are not unit-tested in isolation).
import { describe, it, expect } from 'vitest'
import type { MedarbejderRosterRow } from '../../../hooks/useMedarbejderRoster'
import {
  indexBy,
  childrenOf,
  rootsOf,
  orphansOf,
  isManager,
  descendantsOf,
  depthMap,
  collapsedForLevel,
  defaultCollapsed,
  queryHit,
  visibleTreeRows,
} from '../medarbejderTree'

// --- Fixture builder ----------------------------------------------------------

function row(
  partial: Partial<MedarbejderRosterRow> & Pick<MedarbejderRosterRow, 'employeeId'>,
): MedarbejderRosterRow {
  return {
    displayName: partial.employeeId,
    enhedLabel: 'Enhed',
    position: null,
    structuralApproverId: null,
    periodStatus: 'APPROVED',
    outgoingVikar: null,
    isRoot: false,
    isOrphan: false,
    ...partial,
  }
}

// A small acyclic structural tree:
//   root (isRoot)
//   ├── mgr            (manager of leaf1, leaf2)
//   │   ├── leaf1
//   │   └── leaf2
//   └── solo           (individual report, no children)
//   orphan (isOrphan, no approver, approves no one)
function acyclicTree(): MedarbejderRosterRow[] {
  return [
    row({ employeeId: 'root', displayName: 'Root Person', position: 'Direktør', enhedLabel: 'Direktion', isRoot: true }),
    row({ employeeId: 'mgr', displayName: 'Manager One', position: 'Kontorchef', enhedLabel: 'Drift', structuralApproverId: 'root' }),
    row({ employeeId: 'leaf1', displayName: 'Leaf Alpha', position: 'Tekniker', enhedLabel: 'Netværk', structuralApproverId: 'mgr' }),
    row({ employeeId: 'leaf2', displayName: 'Leaf Beta', position: 'Tekniker', enhedLabel: 'Netværk', structuralApproverId: 'mgr' }),
    row({ employeeId: 'solo', displayName: 'Solo Worker', position: 'Konsulent', enhedLabel: 'Stab', structuralApproverId: 'root' }),
    row({ employeeId: 'orphan', displayName: 'Orphan Person', position: 'Nyansat', enhedLabel: 'Servicedesk', isOrphan: true }),
  ]
}

// --- indexBy / childrenOf / rootsOf / orphansOf / isManager -------------------

describe('indexBy', () => {
  it('maps employeeId to row', () => {
    const people = acyclicTree()
    const idx = indexBy(people)
    expect(idx.mgr.displayName).toBe('Manager One')
    expect(Object.keys(idx)).toHaveLength(people.length)
  })
})

describe('childrenOf', () => {
  it('returns direct reports by structuralApproverId', () => {
    const people = acyclicTree()
    expect(childrenOf(people, 'mgr').map((p) => p.employeeId).sort()).toEqual(['leaf1', 'leaf2'])
    expect(childrenOf(people, 'root').map((p) => p.employeeId).sort()).toEqual(['mgr', 'solo'])
    expect(childrenOf(people, 'leaf1')).toEqual([])
  })
})

describe('rootsOf', () => {
  it('consumes the served isRoot flag', () => {
    const people = acyclicTree()
    expect(rootsOf(people).map((p) => p.employeeId)).toEqual(['root'])
  })
})

describe('orphansOf', () => {
  it('consumes the served isOrphan flag', () => {
    const people = acyclicTree()
    expect(orphansOf(people).map((p) => p.employeeId)).toEqual(['orphan'])
  })
})

describe('isManager', () => {
  it('is true only for people who approve at least one person', () => {
    const people = acyclicTree()
    expect(isManager(people, 'root')).toBe(true)
    expect(isManager(people, 'mgr')).toBe(true)
    expect(isManager(people, 'leaf1')).toBe(false)
    expect(isManager(people, 'solo')).toBe(false)
    expect(isManager(people, 'orphan')).toBe(false)
  })
})

// --- descendantsOf -----------------------------------------------------------

describe('descendantsOf', () => {
  it('returns transitive descendants', () => {
    const people = acyclicTree()
    expect([...descendantsOf(people, 'root')].sort()).toEqual(['leaf1', 'leaf2', 'mgr', 'solo'])
    expect([...descendantsOf(people, 'mgr')].sort()).toEqual(['leaf1', 'leaf2'])
    expect(descendantsOf(people, 'leaf1').size).toBe(0)
  })
})

// --- R4 CYCLE-SAFETY (load-bearing) ------------------------------------------
// A.structuralApproverId = B and B.structuralApproverId = A, neither flagged
// isRoot. An unguarded walk infinite-loops / stack-overflows; the visited-set
// guard must make both descendantsOf AND depthMap TERMINATE.

function cyclicTree(): MedarbejderRosterRow[] {
  return [
    // a self-referential 2-cycle, plus a real root so depthMap actually walks.
    row({ employeeId: 'root', displayName: 'Root', isRoot: true }),
    row({ employeeId: 'A', displayName: 'Aase A', structuralApproverId: 'B' }),
    row({ employeeId: 'B', displayName: 'Bo B', structuralApproverId: 'A' }),
    // give the root a child that points into the cycle so depthMap reaches it.
    row({ employeeId: 'bridge', displayName: 'Bridge', structuralApproverId: 'root' }),
    row({ employeeId: 'C', displayName: 'Cycle Entry', structuralApproverId: 'bridge' }),
  ]
}

describe('R4 cycle-safety', () => {
  it('descendantsOf terminates on a structural cycle', () => {
    const people = cyclicTree()
    // A -> children are people whose approver is A == [B]; B -> [A] (already seen).
    const descA = descendantsOf(people, 'A')
    expect([...descA].sort()).toEqual(['A', 'B'])
    // and it returns rather than hanging — reaching this assertion proves termination.
    const descB = descendantsOf(people, 'B')
    expect([...descB].sort()).toEqual(['A', 'B'])
  })

  it('depthMap terminates on a structural cycle (the R4 invariant)', () => {
    const people = cyclicTree()
    // If depthMap had no visited guard, this would never return / stack-overflow.
    const d = depthMap(people)
    expect(d.root).toBe(0)
    expect(d.bridge).toBe(1)
    expect(d.C).toBe(2)
    // The detached A<->B cycle is unreachable from any root → simply absent,
    // and crucially the call RETURNED.
    expect(d.A).toBeUndefined()
    expect(d.B).toBeUndefined()
  })

  it('depthMap terminates when a ROOT-REACHABLE cycle is walked (the discriminating case)', () => {
    // A genuinely root-reachable A<->B cycle: 'A' is (mis-)flagged isRoot AND sits
    // in the cycle (A.approver=B, B.approver=A). Valid server data can't produce this
    // — a cycle member always HAS an approver, so the server would never flag it
    // isRoot — but the visited guard is exactly the defense for such malformed /
    // future-write-introduced data. The walk STARTS at the root 'A' and steps into
    // the cycle; WITHOUT the guard, walk(A)->walk(B)->walk(A)->... never returns.
    const rootInCycle: MedarbejderRosterRow[] = [
      row({ employeeId: 'A', displayName: 'A', isRoot: true, structuralApproverId: 'B' }),
      row({ employeeId: 'B', displayName: 'B', structuralApproverId: 'A' }),
    ]
    const d = depthMap(rootInCycle)
    expect(d.A).toBe(0) // walked first as a root
    expect(d.B).toBe(1) // its child; the step back into A is cut by the visited guard
    // reaching these assertions at all proves termination (no hang / stack-overflow).
  })
})

// --- depthMap (acyclic) ------------------------------------------------------

describe('depthMap (acyclic)', () => {
  it('assigns 0-based depths from the roots', () => {
    const people = acyclicTree()
    const d = depthMap(people)
    expect(d.root).toBe(0)
    expect(d.mgr).toBe(1)
    expect(d.solo).toBe(1)
    expect(d.leaf1).toBe(2)
    expect(d.leaf2).toBe(2)
    // orphan is not reachable from a root → absent
    expect(d.orphan).toBeUndefined()
  })
})

// --- collapsedForLevel -------------------------------------------------------

describe('collapsedForLevel', () => {
  it('collapses managers at or below the given level boundary', () => {
    const people = acyclicTree()
    // L=1: collapse managers at depth >= 0 → root and mgr (both have children).
    const l1 = collapsedForLevel(people, 1)
    expect([...l1].sort()).toEqual(['mgr', 'root'])
    // L=2: collapse managers at depth >= 1 → mgr only (root is depth 0).
    const l2 = collapsedForLevel(people, 2)
    expect([...l2].sort()).toEqual(['mgr'])
  })

  it('Infinity returns the empty set (show all)', () => {
    const people = acyclicTree()
    expect(collapsedForLevel(people, Infinity).size).toBe(0)
  })
})

// --- defaultCollapsed --------------------------------------------------------

describe('defaultCollapsed', () => {
  it('collapses managers whose reports are all individuals', () => {
    const people = acyclicTree()
    // mgr's reports (leaf1, leaf2) are all non-managers → collapsed.
    // root's reports include mgr (a manager) → NOT collapsed.
    expect([...defaultCollapsed(people)].sort()).toEqual(['mgr'])
  })
})

// --- queryHit ----------------------------------------------------------------

describe('queryHit', () => {
  const p = row({
    employeeId: 'x',
    displayName: 'Mette Holm',
    position: 'Vicedirektør',
    enhedLabel: 'Direktion',
  })

  it('matches on displayName (case-insensitive)', () => {
    expect(queryHit(p, 'mette')).toBe(true)
    expect(queryHit(p, 'HOLM'.toLowerCase())).toBe(true)
  })

  it('matches on position', () => {
    expect(queryHit(p, 'vicedirektør')).toBe(true)
  })

  it('matches on enhedLabel', () => {
    expect(queryHit(p, 'direktion')).toBe(true)
  })

  it('does not match unrelated text', () => {
    expect(queryHit(p, 'netværk')).toBe(false)
  })

  it('tolerates a null position', () => {
    const noPos = row({ employeeId: 'y', displayName: 'Anna', position: null, enhedLabel: 'Drift' })
    expect(queryHit(noPos, 'anna')).toBe(true)
    expect(queryHit(noPos, 'drift')).toBe(true)
  })
})

// --- visibleTreeRows ---------------------------------------------------------

describe('visibleTreeRows', () => {
  it('emits a pre-order walk with depths when nothing is collapsed', () => {
    const people = acyclicTree()
    const rows = visibleTreeRows(people, new Set())
    // pre-order from root: root, mgr, leaf1, leaf2, solo
    expect(rows.map((r) => r.row.employeeId)).toEqual(['root', 'mgr', 'leaf1', 'leaf2', 'solo'])
    expect(rows.map((r) => r.depth)).toEqual([0, 1, 2, 2, 1])
  })

  it('hides the subtree of a collapsed node', () => {
    const people = acyclicTree()
    const rows = visibleTreeRows(people, new Set(['mgr']))
    expect(rows.map((r) => r.row.employeeId)).toEqual(['root', 'mgr', 'solo'])
  })

  it('a visibleSet forces matching subtrees open and filters to the set', () => {
    const people = acyclicTree()
    // even with mgr collapsed, an active visibleSet ignores collapse.
    const visible = new Set(['root', 'mgr', 'leaf1'])
    const rows = visibleTreeRows(people, new Set(['mgr']), visible)
    expect(rows.map((r) => r.row.employeeId)).toEqual(['root', 'mgr', 'leaf1'])
  })

  it('terminates on a ROOT-REACHABLE structural cycle (the discriminating case)', () => {
    // Same malformed shape as the depthMap discriminating test: 'A' is mis-flagged
    // isRoot AND in an A<->B cycle. The walk starts at the root 'A' and steps into
    // the cycle; without the visited guard visibleTreeRows would never return.
    const rootInCycle: MedarbejderRosterRow[] = [
      row({ employeeId: 'A', displayName: 'A', isRoot: true, structuralApproverId: 'B' }),
      row({ employeeId: 'B', displayName: 'B', structuralApproverId: 'A' }),
    ]
    const rows = visibleTreeRows(rootInCycle, new Set())
    // each node emitted exactly once; reaching this assertion proves termination.
    expect(rows.map((r) => r.row.employeeId)).toEqual(['A', 'B'])
  })
})
