// S99 / TASK-9902 — pure tree helpers for the Organisation page. The server
// serves an aggregated 3-tier tree (MAO → Organisation → Enhed leaves); these
// helpers flatten it to render rows, drive the level control + search, and
// supply the type-scoped move targets. No authority/scope logic lives here —
// an Enhed is a FLAT leaf (ADR-035), so Enheder never have children.

import type {
  TreeMaoNode,
  TreeOrganisationNode,
  TreeEnhedNode,
} from '../../hooks/useOrganizationTree'

export type NodeType = 'MAO' | 'ORGANISATION' | 'ENHED'

/** A normalised, render-ready node — the union of the three tier shapes flattened
    to one row contract. `count` is the rolled-up employee count (own + descendants
    for MAO/Organisation) or the Enhed's `taggedUserCount`. `parentId` is the
    immediate parent's id (null for a MAO root). `hasChildren` drives the chevron. */
export interface OrgRow {
  id: string
  name: string
  type: NodeType
  depth: number
  count: number
  parentId: string | null
  /** The owning Organisation id for an Enhed (used to resolve its version). */
  organisationId?: string
  hasChildren: boolean
}

export const TYPE_LABEL: Record<NodeType, string> = {
  MAO: 'Ministeransvarsområde',
  ORGANISATION: 'Organisation',
  ENHED: 'Enhed',
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
    hasChildren: (org.enheder?.length ?? 0) > 0,
  }
}

export function enhedRow(enhed: TreeEnhedNode, org: TreeOrganisationNode): OrgRow {
  return {
    id: enhed.enhedId,
    name: enhed.name,
    type: 'ENHED',
    depth: 2,
    count: enhed.taggedUserCount,
    parentId: org.orgId,
    organisationId: org.orgId,
    hasChildren: false,
  }
}

/** Flatten the tree to the rows currently VISIBLE given the expanded set. A MAO
    is always shown; its organisations show only when the MAO is expanded; an
    organisation's enheder show only when the organisation is expanded. */
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
      if (!expanded.has(org.orgId)) continue
      for (const enhed of org.enheder ?? []) {
        out.push(enhedRow(enhed, org))
      }
    }
  }
  return out
}

/** Every node id, flattened (for "expand to Enhed level" = expand all parents). */
export function allExpandableIds(tree: TreeMaoNode[]): string[] {
  const ids: string[] = []
  for (const mao of tree) {
    if ((mao.organisations?.length ?? 0) > 0) ids.push(mao.orgId)
    for (const org of mao.organisations ?? []) {
      if ((org.enheder?.length ?? 0) > 0) ids.push(org.orgId)
    }
  }
  return ids
}

export type Level = 'MAO' | 'ORGANISATION' | 'ENHED'

/** The expanded set that realises a "Vis til niveau" depth:
      MAO          → roots only (collapse all)
      ORGANISATION → expand every MAO (show organisations) — the default
      ENHED        → expand everything (show enheder) */
export function expandedForLevel(tree: TreeMaoNode[], level: Level): Set<string> {
  if (level === 'MAO') return new Set()
  const set = new Set<string>()
  for (const mao of tree) {
    set.add(mao.orgId)
    if (level === 'ENHED') {
      for (const org of mao.organisations ?? []) set.add(org.orgId)
    }
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
      for (const enhed of org.enheder ?? []) {
        out.push(enhedRow(enhed, org))
      }
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
