// S99 / TASK-9902 — pure tree helpers for the Organisation page. The server
// serves an aggregated tree (MAO → Organisation); these helpers flatten it to
// render rows, drive the level control + search, and supply the move targets.
//
// S103 / TASK-10304 (Enhedsspor Phase 1a) — the Enhed tier is REMOVED. The tree
// is now MAO → Organisation only; no enhed nodes, flattening or move targets.

import type {
  TreeMaoNode,
  TreeOrganisationNode,
} from '../../hooks/useOrganizationTree'

export type NodeType = 'MAO' | 'ORGANISATION'

/** A normalised, render-ready node — the union of the two tier shapes flattened
    to one row contract. `count` is the rolled-up employee count (own +
    descendants). `parentId` is the immediate parent's id (null for a MAO root).
    `hasChildren` drives the chevron. */
export interface OrgRow {
  id: string
  name: string
  type: NodeType
  depth: number
  count: number
  parentId: string | null
  hasChildren: boolean
}

export const TYPE_LABEL: Record<NodeType, string> = {
  MAO: 'Ministeransvarsområde',
  ORGANISATION: 'Organisation',
}

export function maoRow(mao: TreeMaoNode): OrgRow {
  return {
    id: mao.orgId,
    name: mao.orgName,
    type: 'MAO',
    depth: 0,
    count: mao.employeeCount,
    parentId: null,
    hasChildren: (mao.organisations?.length ?? 0) > 0,
  }
}

export function organisationRow(org: TreeOrganisationNode): OrgRow {
  return {
    id: org.orgId,
    name: org.orgName,
    type: 'ORGANISATION',
    depth: 1,
    count: org.employeeCount,
    parentId: org.parentOrgId,
    hasChildren: false,
  }
}

/** Flatten the tree to the rows currently VISIBLE given the expanded set. A MAO
    is always shown; its organisations show only when the MAO is expanded. */
export function visibleRows(
  tree: TreeMaoNode[],
  expanded: Set<string>,
): OrgRow[] {
  const out: OrgRow[] = []
  for (const mao of tree) {
    out.push(maoRow(mao))
    if (!expanded.has(mao.orgId)) continue
    for (const org of mao.organisations ?? []) {
      out.push(organisationRow(org))
    }
  }
  return out
}

/** Every expandable node id, flattened (every MAO that has organisations). */
export function allExpandableIds(tree: TreeMaoNode[]): string[] {
  const ids: string[] = []
  for (const mao of tree) {
    if ((mao.organisations?.length ?? 0) > 0) ids.push(mao.orgId)
  }
  return ids
}

export type Level = 'MAO' | 'ORGANISATION'

/** The expanded set that realises a "Vis til niveau" depth:
      MAO          → roots only (collapse all)
      ORGANISATION → expand every MAO (show organisations) — the default */
export function expandedForLevel(tree: TreeMaoNode[], level: Level): Set<string> {
  if (level === 'MAO') return new Set()
  const set = new Set<string>()
  for (const mao of tree) {
    set.add(mao.orgId)
  }
  return set
}

/** Flatten EVERY node (search mode renders a flat filtered list, no chevrons). */
export function flattenAll(tree: TreeMaoNode[]): OrgRow[] {
  const out: OrgRow[] = []
  for (const mao of tree) {
    out.push(maoRow(mao))
    for (const org of mao.organisations ?? []) {
      out.push(organisationRow(org))
    }
  }
  return out
}

/** Case-insensitive substring search over the flattened node names. */
export function searchRows(tree: TreeMaoNode[], query: string): OrgRow[] {
  const q = query.trim().toLowerCase()
  if (!q) return []
  return flattenAll(tree).filter((r) => r.name.toLowerCase().includes(q))
}

/** Active MAO targets for a move: every MAO EXCEPT the moving org's current
    parent (pre-excluded so the picker never offers a guaranteed-no-op 400). */
export function moveTargets(
  tree: TreeMaoNode[],
  currentParentId: string | null,
): { orgId: string; orgName: string }[] {
  return tree
    .filter((mao) => mao.orgId !== currentParentId)
    .map((mao) => ({ orgId: mao.orgId, orgName: mao.orgName }))
}
