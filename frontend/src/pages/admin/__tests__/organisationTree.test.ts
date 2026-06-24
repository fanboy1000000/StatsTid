// S99 / TASK-9905 — pure-helper tests for the Organisation tree flattening,
// level expansion, search, and move-target derivation.
import { describe, it, expect } from 'vitest'
import type { TreeMaoNode } from '../../../hooks/useOrganizationTree'
import {
  visibleRows,
  flattenAll,
  searchRows,
  expandedForLevel,
  moveTargets,
} from '../organisationTree'

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
        enheder: [{ enhedId: 'ENH01', name: 'Team Drift', taggedUserCount: 5 }],
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
  it('flattenAll yields every node with correct depth', () => {
    const rows = flattenAll(tree)
    expect(rows.map((r) => r.id)).toEqual(['MIN01', 'STY01', 'ENH01', 'MIN02'])
    expect(rows.find((r) => r.id === 'MIN01')!.depth).toBe(0)
    expect(rows.find((r) => r.id === 'STY01')!.depth).toBe(1)
    expect(rows.find((r) => r.id === 'ENH01')!.depth).toBe(2)
  })

  it('an Enhed is a flat leaf (hasChildren false, organisationId set)', () => {
    const enhed = flattenAll(tree).find((r) => r.id === 'ENH01')!
    expect(enhed.hasChildren).toBe(false)
    expect(enhed.organisationId).toBe('STY01')
    expect(enhed.count).toBe(5)
  })

  it('visibleRows shows only expanded subtrees', () => {
    // Nothing expanded → only the MAO roots.
    expect(visibleRows(tree, new Set()).map((r) => r.id)).toEqual(['MIN01', 'MIN02'])
    // Expand MIN01 → its organisation shows; the enhed stays hidden.
    expect(visibleRows(tree, new Set(['MIN01'])).map((r) => r.id)).toEqual([
      'MIN01',
      'STY01',
      'MIN02',
    ])
    // Expand both → the enhed shows.
    expect(visibleRows(tree, new Set(['MIN01', 'STY01'])).map((r) => r.id)).toEqual([
      'MIN01',
      'STY01',
      'ENH01',
      'MIN02',
    ])
  })

  it('expandedForLevel realises the three depths', () => {
    expect([...expandedForLevel(tree, 'MAO')]).toEqual([])
    expect([...expandedForLevel(tree, 'ORGANISATION')].sort()).toEqual(['MIN01', 'MIN02'])
    expect([...expandedForLevel(tree, 'ENHED')].sort()).toEqual(['MIN01', 'MIN02', 'STY01'])
  })

  it('searchRows is case-insensitive substring over names', () => {
    expect(searchRows(tree, 'team').map((r) => r.id)).toEqual(['ENH01'])
    expect(searchRows(tree, 'STYRELSEN').map((r) => r.id)).toEqual(['STY01'])
    expect(searchRows(tree, '')).toEqual([])
  })

  it('moveTargets excludes the current parent MAO', () => {
    // STY01 sits under MIN01 → only MIN02 is a valid target.
    expect(moveTargets(tree, 'MIN01').map((t) => t.orgId)).toEqual(['MIN02'])
    // With no current parent, both MAOs are offered.
    expect(moveTargets(tree, null).map((t) => t.orgId).sort()).toEqual(['MIN01', 'MIN02'])
  })
})
