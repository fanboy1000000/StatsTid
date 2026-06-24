// S99 / TASK-9905 + S100 / TASK-10004 — pure-helper tests for the Organisation
// tree flattening, level expansion, search, and move-target derivation. S100
// makes the enheder HIERARCHICAL (a nested sub-tree + a derived level), so the
// flatten/visible/expand helpers recurse the enhed children, and a new pair of
// helpers (findOrganisation / enhedMoveTargets) drive the enhed re-parent picker.
import { describe, it, expect } from 'vitest'
import type { TreeMaoNode } from '../../../hooks/useOrganizationTree'
import {
  visibleRows,
  flattenAll,
  searchRows,
  expandedForLevel,
  moveTargets,
  findOrganisation,
  enhedMoveTargets,
  enhedSelfAndDescendantIds,
} from '../organisationTree'

// STY01 has a nested enhed sub-tree: ENH01 (root) → ENH02 (child); ENH03 (root).
const tree: TreeMaoNode[] = [
  {
    orgId: 'MIN01',
    orgName: 'Finansministeriet',
    orgType: 'MAO',
    employeeCount: 42,
    organisations: [
      {
        orgId: 'STY01',
        orgName: 'Økonomistyrelsen',
        orgType: 'ORGANISATION',
        parentOrgId: 'MIN01',
        materializedPath: '/MIN01/STY01/',
        employeeCount: 30,
        enheder: [
          {
            enhedId: 'ENH01',
            name: 'Team Drift',
            taggedUserCount: 5,
            parentEnhedId: null,
            level: 1,
            children: [
              {
                enhedId: 'ENH02',
                name: 'Team Netværk',
                taggedUserCount: 2,
                parentEnhedId: 'ENH01',
                level: 2,
                children: [],
              },
            ],
          },
          {
            enhedId: 'ENH03',
            name: 'Team Support',
            taggedUserCount: 3,
            parentEnhedId: null,
            level: 1,
            children: [],
          },
        ],
      },
    ],
  },
  {
    orgId: 'MIN02',
    orgName: 'Skatteministeriet',
    orgType: 'MAO',
    employeeCount: 0,
    organisations: [],
  },
]

describe('organisationTree helpers', () => {
  it('flattenAll yields every node (nested enheder depth-first) with correct depth', () => {
    const rows = flattenAll(tree)
    expect(rows.map((r) => r.id)).toEqual([
      'MIN01',
      'STY01',
      'ENH01',
      'ENH02',
      'ENH03',
      'MIN02',
    ])
    expect(rows.find((r) => r.id === 'MIN01')!.depth).toBe(0)
    expect(rows.find((r) => r.id === 'STY01')!.depth).toBe(1)
    // S100: depth continues past the Organisation = 1 + level.
    expect(rows.find((r) => r.id === 'ENH01')!.depth).toBe(2) // level 1
    expect(rows.find((r) => r.id === 'ENH02')!.depth).toBe(3) // level 2 (child)
    expect(rows.find((r) => r.id === 'ENH03')!.depth).toBe(2) // level 1
  })

  it('an Enhed carries level / parentEnhedId / hasChildren (S100 hierarchy)', () => {
    const rows = flattenAll(tree)
    const e1 = rows.find((r) => r.id === 'ENH01')!
    expect(e1.hasChildren).toBe(true) // has ENH02
    expect(e1.level).toBe(1)
    expect(e1.parentEnhedId).toBeNull()
    expect(e1.organisationId).toBe('STY01')
    expect(e1.count).toBe(5)
    const e2 = rows.find((r) => r.id === 'ENH02')!
    expect(e2.hasChildren).toBe(false)
    expect(e2.level).toBe(2)
    expect(e2.parentEnhedId).toBe('ENH01')
  })

  it('visibleRows shows only expanded subtrees (incl. nested enheder)', () => {
    // Nothing expanded → only the MAO roots.
    expect(visibleRows(tree, new Set()).map((r) => r.id)).toEqual(['MIN01', 'MIN02'])
    // Expand MIN01 → its organisation shows; the enheder stay hidden.
    expect(visibleRows(tree, new Set(['MIN01'])).map((r) => r.id)).toEqual([
      'MIN01',
      'STY01',
      'MIN02',
    ])
    // Expand MIN01 + STY01 → the ROOT enheder show; ENH02 (child) stays hidden.
    expect(visibleRows(tree, new Set(['MIN01', 'STY01'])).map((r) => r.id)).toEqual([
      'MIN01',
      'STY01',
      'ENH01',
      'ENH03',
      'MIN02',
    ])
    // Expand the enhed ENH01 too → its child ENH02 shows.
    expect(
      visibleRows(tree, new Set(['MIN01', 'STY01', 'ENH01'])).map((r) => r.id),
    ).toEqual(['MIN01', 'STY01', 'ENH01', 'ENH02', 'ENH03', 'MIN02'])
  })

  it('expandedForLevel — ENHED expands organisations + intermediate enheder', () => {
    expect([...expandedForLevel(tree, 'MAO')]).toEqual([])
    expect([...expandedForLevel(tree, 'ORGANISATION')].sort()).toEqual(['MIN01', 'MIN02'])
    // ENHED now also expands ENH01 (the only enhed with children).
    expect([...expandedForLevel(tree, 'ENHED')].sort()).toEqual([
      'ENH01',
      'MIN01',
      'MIN02',
      'STY01',
    ])
  })

  it('searchRows is case-insensitive substring over names (nested enheder included)', () => {
    expect(searchRows(tree, 'team').map((r) => r.id).sort()).toEqual(['ENH01', 'ENH02', 'ENH03'])
    expect(searchRows(tree, 'netværk').map((r) => r.id)).toEqual(['ENH02'])
    expect(searchRows(tree, 'STYRELSEN').map((r) => r.id)).toEqual(['STY01'])
    expect(searchRows(tree, '')).toEqual([])
  })

  it('moveTargets excludes the current parent MAO', () => {
    // STY01 sits under MIN01 → only MIN02 is a valid target.
    expect(moveTargets(tree, 'MIN01').map((t) => t.orgId)).toEqual(['MIN02'])
    // With no current parent, both MAOs are offered.
    expect(moveTargets(tree, null).map((t) => t.orgId).sort()).toEqual(['MIN01', 'MIN02'])
  })

  it('findOrganisation locates an Organisation node anywhere in the tree', () => {
    expect(findOrganisation(tree, 'STY01')!.orgName).toBe('Økonomistyrelsen')
    expect(findOrganisation(tree, 'NOPE')).toBeNull()
    // MAOs are not Organisations.
    expect(findOrganisation(tree, 'MIN01')).toBeNull()
  })

  it('enhedSelfAndDescendantIds collects the enhed + its whole subtree', () => {
    const org = findOrganisation(tree, 'STY01')
    expect([...enhedSelfAndDescendantIds(org!.enheder, 'ENH01')].sort()).toEqual([
      'ENH01',
      'ENH02',
    ])
    expect([...enhedSelfAndDescendantIds(org!.enheder, 'ENH03')]).toEqual(['ENH03'])
  })

  it('enhedMoveTargets excludes self + descendants + the current parent', () => {
    const org = findOrganisation(tree, 'STY01')
    // Move ENH01 (parent null): excluded = ENH01 (self) + ENH02 (descendant) →
    // only ENH03 is a valid target.
    expect(enhedMoveTargets(org, 'ENH01', null).map((t) => t.enhedId)).toEqual(['ENH03'])
    // Move ENH02 (current parent ENH01): excluded = ENH02 (self) + ENH01 (current
    // parent, a no-op) → only ENH03.
    expect(enhedMoveTargets(org, 'ENH02', 'ENH01').map((t) => t.enhedId)).toEqual(['ENH03'])
    // Move ENH03 (a leaf root): ENH01 + ENH02 are valid.
    expect(enhedMoveTargets(org, 'ENH03', null).map((t) => t.enhedId).sort()).toEqual([
      'ENH01',
      'ENH02',
    ])
    // A null org → no targets.
    expect(enhedMoveTargets(null, 'ENH01', null)).toEqual([])
  })
})
